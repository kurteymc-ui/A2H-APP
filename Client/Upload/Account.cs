using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AionDPS.Upload;

/// <summary>
/// The meter's sign-in to AION 2 Helper (the player's Discord on the site). Every fight on the site has an owner who decides who may see it,
/// so the meter uploads only when signed in. Signing in: the meter asks the site for a short code (link/start), opens
/// aion2helper.com/meter/link?code=... in the browser, where the player, signed in with Discord, approves it; the meter asks every few
/// seconds (link/poll) and gets a token of its own once approved. The token is kept in the data folder, encrypted for this Windows user
/// (DPAPI), and sent as "Authorization: Bearer" with every upload. The site's Discord page lists every signed-in PC and can sign one out.
/// </summary>
public static class Account
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.DataFolder, "account.dat");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly object Gate = new();

    private static string? _token;

    /// <summary>The Discord name the meter is signed in as (null when unknown or signed out).</summary>
    public static string? Name { get; private set; }

    public static bool SignedIn => Token is not null;

    internal static string? Token
    {
        get { lock (Gate) { return _token; } }
    }

    /// <summary>Raised after signing in or out (not on the UI thread).</summary>
    public static event Action? Changed;

    static Account()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string[] parts = Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(FilePath))).Split('\n', 2);
                _token = parts[0].Length > 0 ? parts[0] : null;
                Name = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            }
        }
        catch (Exception)
        {
            // another Windows user's file, or a damaged one: signed out
            _token = null;
        }
    }

    private static void Save(string? token, string? name)
    {
        lock (Gate)
        {
            _token = token;
            Name = token is null ? null : name;
            try
            {
                if (token is null)
                {
                    File.Delete(FilePath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.WriteAllBytes(FilePath, Dpapi.Protect(Encoding.UTF8.GetBytes(token + "\n" + (name ?? ""))));
                }
            }
            catch (Exception)
            {
                // the sign-in still holds for this run
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Adds the sign-in to a request to the site.</summary>
    internal static void Authorize(HttpRequestMessage request)
    {
        string? token = Token;
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>The site answered 401 with the stored token: it was signed out on the site (Discord page, "Sign out"), so forget it.</summary>
    internal static void Rejected(string? usedToken)
    {
        if (usedToken is not null && usedToken == Token)
        {
            Save(null, null);
        }
    }

    public sealed record LinkStart(string Code, string Poll, string Url, DateTime ExpiresAt, int Interval);

    public enum LinkState { Waiting, Linked, Expired, Failed }

    /// <summary>Asks the site for a sign-in code. Null with the reason when the site cannot be reached.</summary>
    public static async Task<(LinkStart? Start, string? Error)> StartAsync()
    {
        try
        {
            using HttpResponseMessage r = await Http.PostAsJsonAsync($"{Brand.Api}/api/link/start", new { label = Environment.MachineName });
            using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;
            if (!r.IsSuccessStatusCode)
            {
                return (null, ErrorOf(root, r));
            }

            return (new LinkStart(root.GetProperty("code").GetString()!, root.GetProperty("poll").GetString()!, root.GetProperty("url").GetString()!,
                root.TryGetProperty("expiresAt", out JsonElement e) && e.TryGetDateTime(out DateTime at) ? at.ToUniversalTime() : DateTime.UtcNow.AddMinutes(15),
                root.TryGetProperty("interval", out JsonElement i) && i.TryGetInt32(out int s) ? Math.Clamp(s, 2, 30) : 3), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, "The site can’t be reached: " + ex.Message);
        }
    }

    /// <summary>One look whether the sign-in was approved; on Linked the meter is signed in from then on.</summary>
    public static async Task<LinkState> PollAsync(string poll)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(await Http.GetStringAsync($"{Brand.Api}/api/link/poll?poll={Uri.EscapeDataString(poll)}"));
            JsonElement root = doc.RootElement;
            string status = root.TryGetProperty("status", out JsonElement st) ? st.GetString() ?? "" : "";
            if (status == "linked" && root.TryGetProperty("token", out JsonElement tk) && tk.GetString() is { Length: > 0 } token)
            {
                Save(token, root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
                return LinkState.Linked;
            }

            return status == "expired" ? LinkState.Expired : LinkState.Waiting;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return LinkState.Failed;
        }
    }

    /// <summary>Asks the site who the meter is signed in as (refreshes <see cref="Name"/>; a sign-out made on the site is noticed here).</summary>
    public static async Task RefreshAsync()
    {
        string? token = Token;
        if (token is null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Brand.Api}/api/me");
            Authorize(request);
            using HttpResponseMessage r = await Http.SendAsync(request);
            if ((int)r.StatusCode == 401)
            {
                Rejected(token);
                return;
            }

            if (r.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String && n.GetString() != Name)
                {
                    Save(token, n.GetString());
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // offline: keep the sign-in
        }
    }

    /// <summary>Signs this PC out, on the site too.</summary>
    public static async Task SignOutAsync()
    {
        string? token = Token;
        Save(null, null);
        if (token is null)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Brand.Api}/api/logout") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage _ = await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // offline: the site's Discord page can still sign this PC out
        }
    }

    /// <summary>The site's error text from an answer like {"ok":false,"error":"..."}.</summary>
    internal static string ErrorOf(JsonElement root, HttpResponseMessage r) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : $"{(int)r.StatusCode} {r.ReasonPhrase}";

    /// <summary>Windows' own encryption for the current user (CryptProtectData), so another user of the PC can't read the token.</summary>
    private static class Dpapi
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Blob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref Blob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        private const int UiForbidden = 0x1;

        public static byte[] Protect(byte[] data) => Run(data, true);

        public static byte[] Unprotect(byte[] data) => Run(data, false);

        private static byte[] Run(byte[] data, bool protect)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            var input = new Blob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var output = new Blob();
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref input, Brand.AppName, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
                if (!ok)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }

                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                handle.Free();
                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }
    }
}
