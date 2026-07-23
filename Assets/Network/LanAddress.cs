using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

// Elige la IPv4 de LAN "real" del dispositivo para compartir con otros jugadores,
// evitando adaptadores virtuales (Radmin VPN, Hamachi, VMware, VirtualBox, WSL,
// Docker, TAP/TUN, Bluetooth PAN).
//
// El problema: en Windows con Radmin VPN instalado, el adaptador virtual (rango
// 26.x.x.x, que NO es privado RFC1918) suele salir primero al iterar interfaces,
// y era la IP que se mostraba — inútil para que otros se conecten por la red real.
//
// Heurística por puntaje:
//   + rango privado RFC1918 (192.168 / 10.x / 172.16–31): +100 / +80
//   + la interfaz tiene gateway (está realmente enrutando a una red): +50
//   + tipo WiFi / Ethernet: +45 / +40
//   − nombre de adaptador virtual/VPN conocido: −120
// Radmin (26.x) no es privado y su adaptador se llama "Radmin VPN" ⇒ queda en
// negativo y una 192.168.x normal gana siempre.
//
// Caso "hotspot del celular": si el host es una PC conectada al WiFi que comparte
// un celular con datos móviles, esa interfaz es WiFi, con gateway (el celular) y
// una IP privada (típicamente 192.168.x) ⇒ gana sin configurar nada. Las demás
// personas se conectan a esa IP dentro de la red del hotspot.
public static class LanAddress
{
    private static readonly string[] VirtualKeywords =
    {
        "radmin", "hamachi", "zerotier", "vpn", "virtual", "vmware", "vbox",
        "virtualbox", "hyper-v", "loopback", "pseudo", "tap", "tun-", "tunnel",
        "docker", "wsl", "bluetooth",
    };

    // Mejor IP para compartir, o "?.?.?.?" si no se encontró ninguna.
    public static string BestLanIPv4()
    {
        var all = Ranked();
        return all.Count > 0 ? all[0] : "?.?.?.?";
    }

    // Todas las IPv4 candidatas ordenadas de mejor a peor (para ofrecer
    // alternativas si la principal no es la de la red esperada).
    public static List<string> AllLanIPv4() => Ranked();

    private static List<string> Ranked()
    {
        var scored = new List<(string ip, int score)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)   continue;

                string tag = ((ni.Name ?? "") + " " + (ni.Description ?? "")).ToLowerInvariant();
                bool esVirtual = false;
                foreach (var kw in VirtualKeywords)
                    if (tag.Contains(kw)) { esVirtual = true; break; }

                bool tieneGateway = false;
                try
                {
                    foreach (var g in ni.GetIPProperties().GatewayAddresses)
                        if (g?.Address != null &&
                            g.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !g.Address.Equals(IPAddress.Any))
                        { tieneGateway = true; break; }
                }
                catch { /* GatewayAddresses puede no estar disponible en móvil */ }

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip)) continue;
                    var b = ip.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue; // APIPA (sin DHCP)

                    int score = 0;
                    bool priv192 = b[0] == 192 && b[1] == 168;
                    bool priv10  = b[0] == 10;
                    bool priv172 = b[0] == 172 && b[1] >= 16 && b[1] <= 31;
                    if (priv192)                 score += 100;
                    else if (priv10 || priv172)  score += 80;
                    // No privado (Radmin 26.x, IP pública, etc.): sin bonus.

                    if (tieneGateway) score += 50;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score += 45;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 40;
                    if (esVirtual) score -= 120;

                    scored.Add((ip.ToString(), score));
                }
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[LanAddress] {e.Message}"); }

        scored.Sort((a, b) => b.score.CompareTo(a.score));
        var result = new List<string>(scored.Count);
        foreach (var s in scored) result.Add(s.ip);
        return result;
    }
}
