using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using ZstdSharp;

namespace LogicAppStorageInspector.Services
{
    // Decodes Logic Apps compressed content: LEB128 marker + ZStandard/Deflate frame + ContentLink wrapper.
    public static class ScanEngine
    {
        public static string DecodeField(byte[] data, BlobServiceClient blobSvc)
        {
            if (data == null || data.Length == 0) return null;
            try
            {
                int marker = data[0] & 0x7;
                string contentLinkJson;
                if (marker == 7)
                {
                    int h = 0; foreach (var b in data) { h++; if ((b & 0x80) == 0) break; }
                    var frame = data.AsSpan(h).ToArray();
                    using var d = new Decompressor();
                    contentLinkJson = Encoding.UTF8.GetString(d.Unwrap(frame).ToArray());
                }
                else if (marker == 6)
                {
                    return "(LZ4-compressed content - not decoded)";
                }
                else
                {
                    using var ms = new MemoryStream(data);
                    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(ds, Encoding.UTF8);
                    contentLinkJson = sr.ReadToEnd();
                }
                return ExtractContent(contentLinkJson, blobSvc);
            }
            catch (Exception ex) { return "(decode error: " + ex.Message + ")"; }
        }

        private static string ExtractContent(string contentLinkJson, BlobServiceClient blobSvc)
        {
            try
            {
                using var doc = JsonDocument.Parse(contentLinkJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("inlinedContent", out var ic) && ic.ValueKind == JsonValueKind.String)
                    return Encoding.UTF8.GetString(Convert.FromBase64String(ic.GetString()));
                if (root.TryGetProperty("uri", out var uriEl) && uriEl.ValueKind == JsonValueKind.String)
                {
                    var uri = uriEl.GetString();
                    return TryFetchBlob(uri, blobSvc) ?? ("(content stored in blob: " + uri + ")");
                }
                if (root.TryGetProperty("nestedContentLinks", out var nested) && nested.ValueKind == JsonValueKind.Object)
                {
                    JsonElement link;
                    if (nested.TryGetProperty("body", out link) || nested.TryGetProperty("root", out link))
                        return ExtractContent(link.GetRawText(), blobSvc);
                }
                return contentLinkJson;
            }
            catch { return contentLinkJson; }
        }

        private static string TryFetchBlob(string uri, BlobServiceClient blobSvc)
        {
            if (blobSvc == null || string.IsNullOrEmpty(uri)) return null;
            try
            {
                var u = new Uri(uri);
                var path = u.AbsolutePath;
                if (path.StartsWith("/")) path = path.Substring(1);
                var parts = path.Split(new[] { "/" }, 2, StringSplitOptions.None);
                if (parts.Length < 2) return null;
                var bc = blobSvc.GetBlobContainerClient(parts[0]).GetBlobClient(Uri.UnescapeDataString(parts[1]));
                var raw = bc.DownloadContent().Value.Content.ToArray();
                return TryText(raw);
            }
            catch { return null; }
        }

        private static string TryText(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return "";
            try
            {
                int marker = raw[0] & 0x7;
                if (marker == 7)
                {
                    int h = 0; foreach (var b in raw) { h++; if ((b & 0x80) == 0) break; }
                    using var d = new Decompressor();
                    var s = Encoding.UTF8.GetString(d.Unwrap(raw.AsSpan(h).ToArray()).ToArray());
                    try { using var doc = JsonDocument.Parse(s); if (doc.RootElement.TryGetProperty("inlinedContent", out var ic) && ic.ValueKind == JsonValueKind.String) return Encoding.UTF8.GetString(Convert.FromBase64String(ic.GetString())); } catch { }
                    return s;
                }
            }
            catch { }
            try { using var ms = new MemoryStream(raw); using var ds = new DeflateStream(ms, CompressionMode.Decompress); using var sr = new StreamReader(ds); return sr.ReadToEnd(); } catch { }
            return Encoding.UTF8.GetString(raw);
        }

        public static string Pretty(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try { using var doc = JsonDocument.Parse(s); return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
            catch { return s; }
        }

        public static string AsText(TableEntity e, string key)
        {
            if (!e.ContainsKey(key)) return "";
            var v = e[key];
            if (v == null) return "";
            if (v is DateTimeOffset dto) return dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
            return v.ToString();
        }
    }
}