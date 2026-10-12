using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LanGameHub;

/// <summary>A player found on the network.</summary>
public class Peer
{
    public string Id;
    public string Name;
    public string Ip;
    public int Port;
    public DateTime LastSeen;

    public override string ToString() => Name + "  (" + Ip + ")";
}

/// <summary>One TCP connection to another player.</summary>
class Conn
{
    public TcpClient Client;
    public StreamWriter Writer;
    public string PeerId;
}

/// <summary>
/// Handles everything about the network:
///  - UDP broadcast so players on the same Wi-Fi / LAN find each other
///  - TCP connections for chat, invitations and game moves
/// Message format on the wire is one text line:  TYPE|payload
/// </summary>
public class NetworkService : IDisposable
{
    const int DiscoveryPort = 45454;

    public readonly string MyId = Guid.NewGuid().ToString("N");
    public string MyName = Environment.MachineName;

    readonly Dictionary<string, Peer> peers = new();
    readonly Dictionary<string, Conn> conns = new();
    readonly BlockingCollection<(string peerId, string type, string payload)> sendQueue = new();

    TcpListener listener;
    UdpClient udpListen;
    UdpClient udpSend;
    volatile bool running;
    int tcpPort;

    public event Action PeersChanged;
    public event Action<string, string, string> MessageReceived; // peerId, type, payload
    public event Action<string> SendFailed;                      // peerId

    // ---------------------------------------------------------------- start / stop

    public void Start()
    {
        running = true;

        listener = new TcpListener(IPAddress.Any, 0); // any free port
        listener.Start();
        tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        udpListen = new UdpClient();
        udpListen.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpListen.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        udpSend = new UdpClient();
        udpSend.EnableBroadcast = true;

        StartThread(AcceptLoop);
        StartThread(ListenLoop);
        StartThread(BroadcastLoop);
        StartThread(SendLoop);
    }

    static void StartThread(ThreadStart work)
    {
        new Thread(work) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        running = false;
        try { sendQueue.CompleteAdding(); } catch { }
        try { listener?.Stop(); } catch { }
        try { udpListen?.Close(); } catch { }
        try { udpSend?.Close(); } catch { }
        lock (conns)
        {
            foreach (var c in conns.Values)
            {
                try { c.Client.Close(); } catch { }
            }
            conns.Clear();
        }
    }

    // ---------------------------------------------------------------- public API

    public List<Peer> GetPeers()
    {
        lock (peers) return peers.Values.OrderBy(p => p.Name).ToList();
    }

    public Peer GetPeer(string id)
    {
        lock (peers) return peers.TryGetValue(id, out var p) ? p : null;
    }

    public void Send(string peerId, string type, string payload)
    {
        try { sendQueue.Add((peerId, type, payload ?? "")); } catch { }
    }

    // ---------------------------------------------------------------- discovery (UDP)

    // A trailing "|1" asks the receiver to answer straight away.
    byte[] AnnounceData(bool askReply) =>
        Encoding.UTF8.GetBytes("LANHUB|" + MyId + "|" + tcpPort + "|" + MyName + (askReply ? "|1" : ""));

    /// <summary>Broadcasts "I am here" to the whole local network.</summary>
    void Announce(bool askReply)
    {
        byte[] data = AnnounceData(askReply);
        foreach (var ep in BroadcastTargets())
        {
            try { udpSend.Send(data, data.Length, ep); } catch { }
        }
    }

    /// <summary>
    /// Sends the announcement directly to every address of the local subnet (up to 254 PCs).
    /// This still works on routers / Wi-Fi networks that block UDP broadcast.
    /// </summary>
    void Sweep()
    {
        byte[] data = AnnounceData(true);
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    byte[] ip = ua.Address.GetAddressBytes();
                    byte[] mask = ua.IPv4Mask.GetAddressBytes();
                    // never sweep more than one /24 block
                    mask[0] = 255; mask[1] = 255; mask[2] = 255;
                    int hostMax = ~mask[3] & 0xFF;
                    int net3 = ip[3] & mask[3];
                    for (int h = 1; h < hostMax; h++)
                    {
                        int last = net3 + h;
                        if (last == ip[3]) continue;
                        var target = new IPEndPoint(new IPAddress(new byte[] { ip[0], ip[1], ip[2], (byte)last }), DiscoveryPort);
                        try { udpSend.Send(data, data.Length, target); } catch { }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>Forgets all players and searches the network again.</summary>
    public void Refresh()
    {
        lock (peers) peers.Clear();
        PeersChanged?.Invoke();
        StartThread(() =>
        {
            try
            {
                Announce(true);
                Sweep();
            }
            catch { }
        });
    }

    /// <summary>The IPv4 address of this PC on the Wi-Fi / LAN (the one that has a gateway).</summary>
    public static string GetMyIp()
    {
        string fallback = "unknown";
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (hasGateway) return ua.Address.ToString();
                    if (fallback == "unknown") fallback = ua.Address.ToString();
                }
            }
        }
        catch { }
        return fallback;
    }

    void BroadcastLoop()
    {
        int tick = 0;
        while (running)
        {
            try
            {
                Announce(false);
                if (tick % 10 == 0) Sweep();   // first time and then every 20 seconds
                Prune();
            }
            catch { }
            tick++;
            Thread.Sleep(2000);
        }
    }

    static List<IPEndPoint> BroadcastTargets()
    {
        var list = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, DiscoveryPort) };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    byte[] ip = ua.Address.GetAddressBytes();
                    byte[] mask = ua.IPv4Mask.GetAddressBytes();
                    var bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                    list.Add(new IPEndPoint(new IPAddress(bc), DiscoveryPort));
                }
            }
        }
        catch { }
        return list;
    }

    void ListenLoop()
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udpListen.Receive(ref ep);
                string[] p = Encoding.UTF8.GetString(data).Split('|');
                if (p.Length < 4 || p[0] != "LANHUB" || p[1] == MyId) continue;
                if (!int.TryParse(p[2], out int port)) continue;
                bool askReply = p.Length >= 5 && p[4] == "1";

                string ip = ep.Address.ToString();
                bool isNew = false;
                bool changed = false;
                lock (peers)
                {
                    if (!peers.TryGetValue(p[1], out var peer))
                    {
                        peer = new Peer { Id = p[1] };
                        peers[p[1]] = peer;
                        changed = true;
                        isNew = true;
                    }
                    if (peer.Name != p[3] || peer.Ip != ip || peer.Port != port) changed = true;
                    peer.Name = p[3];
                    peer.Ip = ip;
                    peer.Port = port;
                    peer.LastSeen = DateTime.UtcNow;
                }
                if (changed) PeersChanged?.Invoke();

                // answer directly so the other player finds us at once
                if (isNew || askReply)
                {
                    try
                    {
                        byte[] mine = AnnounceData(false);
                        udpSend.Send(mine, mine.Length, new IPEndPoint(ep.Address, DiscoveryPort));
                    }
                    catch { }
                }
            }
            catch
            {
                if (!running) break;
            }
        }
    }

    void Prune()
    {
        bool changed = false;
        lock (peers)
        {
            var old = peers.Values.Where(p => (DateTime.UtcNow - p.LastSeen).TotalSeconds > 8).Select(p => p.Id).ToList();
            foreach (var id in old)
            {
                peers.Remove(id);
                changed = true;
            }
        }
        if (changed) PeersChanged?.Invoke();
    }

    // ---------------------------------------------------------------- connections (TCP)

    void AcceptLoop()
    {
        while (running)
        {
            try
            {
                TcpClient c = listener.AcceptTcpClient();
                Attach(c, null);
            }
            catch
            {
                if (!running) break;
            }
        }
    }

    Conn Attach(TcpClient client, string peerId)
    {
        var conn = new Conn { Client = client, PeerId = peerId };
        conn.Writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        if (peerId != null)
        {
            lock (conns) conns[peerId] = conn;
        }
        WriteLine(conn, "HELLO|" + MyId + "|" + MyName);
        StartThread(() => ReadLoop(conn));
        return conn;
    }

    static void WriteLine(Conn conn, string line)
    {
        lock (conn)
        {
            conn.Writer.WriteLine(line);
        }
    }

    void ReadLoop(Conn conn)
    {
        try
        {
            var reader = new StreamReader(conn.Client.GetStream(), Encoding.UTF8);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string[] parts = line.Split('|', 2);
                string type = parts[0];
                string payload = parts.Length > 1 ? parts[1] : "";

                if (type == "HELLO")
                {
                    string[] h = payload.Split('|', 2);
                    conn.PeerId = h[0];
                    lock (conns) conns[h[0]] = conn;
                    if (h.Length > 1) UpdateName(h[0], h[1]);
                }
                else if (conn.PeerId != null)
                {
                    MessageReceived?.Invoke(conn.PeerId, type, payload);
                }
            }
        }
        catch { }
        finally
        {
            Drop(conn);
        }
    }

    void UpdateName(string id, string name)
    {
        bool changed = false;
        lock (peers)
        {
            if (peers.TryGetValue(id, out var p) && p.Name != name)
            {
                p.Name = name;
                changed = true;
            }
        }
        if (changed) PeersChanged?.Invoke();
    }

    void Drop(Conn conn)
    {
        lock (conns)
        {
            if (conn.PeerId != null && conns.TryGetValue(conn.PeerId, out var cur) && cur == conn)
                conns.Remove(conn.PeerId);
        }
        try { conn.Client.Close(); } catch { }
    }

    Conn GetConn(string peerId)
    {
        lock (conns)
        {
            if (conns.TryGetValue(peerId, out var existing) && existing.Client.Connected)
                return existing;
        }

        Peer peer = GetPeer(peerId);
        if (peer == null) throw new IOException("Player not found");

        var client = new TcpClient();
        if (!client.ConnectAsync(peer.Ip, peer.Port).Wait(3000) || !client.Connected)
        {
            try { client.Close(); } catch { }
            throw new IOException("Cannot connect");
        }
        return Attach(client, peerId);
    }

    void SendLoop()
    {
        foreach (var m in sendQueue.GetConsumingEnumerable())
        {
            string line = m.type + "|" + m.payload.Replace("\r", " ").Replace("\n", " ");
            bool sent = false;
            for (int attempt = 0; attempt < 2 && !sent; attempt++)
            {
                try
                {
                    Conn conn = GetConn(m.peerId);
                    WriteLine(conn, line);
                    sent = true;
                }
                catch
                {
                    lock (conns)
                    {
                        if (conns.TryGetValue(m.peerId, out var bad))
                        {
                            conns.Remove(m.peerId);
                            try { bad.Client.Close(); } catch { }
                        }
                    }
                }
            }
            if (!sent) SendFailed?.Invoke(m.peerId);
        }
    }
}
