using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    public sealed record SteamIdentity(string SteamId, string PersonaName);

    /// <summary>
    /// "Sign in through Steam" using Steam's OpenID 2.0 provider. The user signs in on steamcommunity.com
    /// in their browser; the launcher only receives the SteamID64, and confirms it with Steam before trusting it.
    /// </summary>
    public static class SteamAuthService
    {
        private const string Provider = "https://steamcommunity.com/openid/login";
        private static readonly Regex ClaimedIdRx = new(@"^https?://steamcommunity\.com/openid/id/(7656119\d{10})$");

        public static async Task<SteamIdentity> SignInAsync(CancellationToken ct)
        {
            string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string realm = $"http://127.0.0.1:{port}/";
                string returnTo = $"{realm}steam/{state}";
                var query = new Dictionary<string, string>
                {
                    ["openid.ns"] = "http://specs.openid.net/auth/2.0",
                    ["openid.mode"] = "checkid_setup",
                    ["openid.return_to"] = returnTo,
                    ["openid.realm"] = realm,
                    ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
                    ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
                };
                string url = Provider + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));

                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var stream = client.GetStream();
                    string? target = await ReadRequestTargetAsync(stream, timeout.Token);
                    if (target == null || !target.StartsWith($"/steam/{state}?", StringComparison.Ordinal))
                    {
                        await RespondAsync(stream, 404, "Not found", "");
                        continue;
                    }

                    var p = ParseQuery(target[(target.IndexOf('?') + 1)..]);
                    try
                    {
                        if (p.GetValueOrDefault("openid.mode") == "cancel")
                            throw new OperationCanceledException("Sign-in was cancelled on Steam.");
                        if (p.GetValueOrDefault("openid.mode") != "id_res")
                            throw new InvalidOperationException("Steam did not complete the sign-in.");
                        if (p.GetValueOrDefault("openid.return_to") != returnTo)
                            throw new InvalidOperationException("The sign-in response didn't match this request.");
                        var m = ClaimedIdRx.Match(p.GetValueOrDefault("openid.claimed_id") ?? "");
                        if (!m.Success)
                            throw new InvalidOperationException("Steam returned an unexpected account id.");
                        if (!await VerifyWithSteamAsync(p, timeout.Token))
                            throw new InvalidOperationException("Steam could not confirm the sign-in. Try again.");

                        string steamId = m.Groups[1].Value;
                        string persona = await GetPersonaNameAsync(steamId) ?? "";
                        await RespondAsync(stream, 200, "Signed in",
                            "You're signed in to Swerve2k Downloader. You can close this tab and go back to the launcher.");
                        return new SteamIdentity(steamId, persona);
                    }
                    catch (Exception ex)
                    {
                        await RespondAsync(stream, 200, "Sign-in failed", ex.Message);
                        throw;
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task<bool> VerifyWithSteamAsync(Dictionary<string, string> p, CancellationToken ct)
        {
            var form = p.Where(kv => kv.Key.StartsWith("openid.", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            form["openid.mode"] = "check_authentication";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var resp = await http.PostAsync(Provider, new FormUrlEncodedContent(form), ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode && body.Contains("is_valid:true", StringComparison.Ordinal);
        }

        /// <summary>Public persona name from the Steam community profile (no API key needed).</summary>
        public static async Task<string?> GetPersonaNameAsync(string steamId)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}/?xml=1");
                var m = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.Singleline);
                return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
            }
            catch { return null; }
        }

        public static string ProfileUrl(string steamId) => $"https://steamcommunity.com/profiles/{steamId}";

        /// <summary>Accepts a SteamID64 or a steamcommunity.com/profiles/ link.</summary>
        public static string? ParseSteamId(string input)
        {
            var m = Regex.Match(input?.Trim() ?? "", @"(7656119\d{10})");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[16384];
            var sb = new StringBuilder();
            while (!sb.ToString().Contains("\r\n\r\n") && sb.Length < 65536)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            string firstLine = sb.ToString().Split("\r\n")[0];
            var parts = firstLine.Split(' ');
            return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                string value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                result[key] = value;
            }
            return result;
        }

        private static async Task RespondAsync(Stream stream, int status, string title, string message)
        {
            string html = $@"<!doctype html><html><head><meta charset=""utf-8""><title>{WebUtility.HtmlEncode(title)}</title>
<style>body{{margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0a0a0a;color:#f5f5f5;font-family:'Segoe UI',sans-serif}}
.card{{max-width:420px;padding:32px;border:1px solid #2a2a2a;border-radius:14px;background:#141414}}h1{{font-size:20px;margin:0 0 8px}}p{{color:#a3a3a3;margin:0;line-height:1.5}}</style>
</head><body><div class=""card""><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(message)}</p></div></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            string reason = status == 200 ? "OK" : "Not Found";
            byte[] head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            try
            {
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch { }
        }
    }
}
