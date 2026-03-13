using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneBotPlugin
{
    public static class CQCodeParser
    {
        public static string ExtractPlainText(JToken? message)
        {
            if (message is null) return "";

            // String format: raw CQ-coded message
            if (message.Type == JTokenType.String)
                return ExtractPlainTextFromCQ(message.ToString());

            // Array format: message segments
            if (message.Type == JTokenType.Array)
                return ExtractPlainTextFromSegments(message.ToObject<List<OneBotMessageSegment>>());

            return message.ToString();
        }

        public static string ExtractPlainTextFromCQ(string raw)
        {
            // Remove all CQ codes, keep plain text
            var text = Regex.Replace(raw, @"\[CQ:[^\]]+\]", "");
            return text.Trim();
        }

        public static string ExtractPlainTextFromSegments(List<OneBotMessageSegment>? segments)
        {
            if (segments is null || segments.Count == 0) return "";

            var parts = new List<string>();
            foreach (var seg in segments)
            {
                switch (seg.Type)
                {
                    case "text":
                        var text = seg.Data?.Value<string>("text");
                        if (!string.IsNullOrEmpty(text))
                            parts.Add(text);
                        break;
                    // Ignore at/image/face/etc for text extraction
                }
            }
            return string.Join("", parts).Trim();
        }

        public static bool ContainsAt(JToken? message, string targetQQ)
        {
            if (string.IsNullOrEmpty(targetQQ) || message is null) return false;

            if (message.Type == JTokenType.String)
            {
                var raw = message.ToString();
                return raw.Contains($"[CQ:at,qq={targetQQ}]") || raw.Contains("[CQ:at,qq=all]");
            }

            if (message.Type == JTokenType.Array)
            {
                var segments = message.ToObject<List<OneBotMessageSegment>>();
                if (segments is null) return false;
                return segments.Any(s =>
                    s.Type == "at" &&
                    s.Data is not null &&
                    (s.Data.Value<string>("qq") == targetQQ || s.Data.Value<string>("qq") == "all"));
            }

            return false;
        }

        public static object BuildTextMessage(string text)
        {
            return new[]
            {
                new { type = "text", data = new { text } }
            };
        }

        /// <summary>
        /// Parse text that may contain CQ codes into proper message segments.
        /// e.g., "[CQ:at,qq=123] hello" → [at segment, text segment]
        /// Plain text without CQ codes → [text segment]
        /// </summary>
        public static object ParseTextToSegments(string text)
        {
            if (!text.Contains("[CQ:"))
                return BuildTextMessage(text);

            var segments = new List<object>();
            var regex = new Regex(@"\[CQ:(\w+)((?:,[^,\]]+)*)\]");
            int lastIndex = 0;

            foreach (Match match in regex.Matches(text))
            {
                if (match.Index > lastIndex)
                {
                    var preceding = text[lastIndex..match.Index];
                    if (preceding.Length > 0)
                        segments.Add(new { type = "text", data = (object)new { text = preceding } });
                }

                var cqType = match.Groups[1].Value;
                var paramsStr = match.Groups[2].Value;
                var data = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(paramsStr))
                {
                    foreach (var param in paramsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var eqIdx = param.IndexOf('=');
                        if (eqIdx > 0)
                            data[param[..eqIdx].Trim()] = param[(eqIdx + 1)..].Trim();
                    }
                }
                segments.Add(new { type = cqType, data = (object)data });
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                var trailing = text[lastIndex..];
                if (trailing.Length > 0)
                    segments.Add(new { type = "text", data = (object)new { text = trailing } });
            }

            return segments.Count > 0 ? segments.ToArray() : BuildTextMessage(text);
        }

        /// <summary>
        /// Parse text (with optional CQ codes) and optionally append an image segment.
        /// </summary>
        public static object ParseTextWithOptionalImage(string text, string? image)
        {
            if (string.IsNullOrEmpty(image))
                return ParseTextToSegments(text);

            var file = NormalizeImageSource(image);
            var baseSegments = ParseTextToSegments(text);
            var list = (baseSegments is object[] arr) ? new List<object>(arr) : new List<object> { baseSegments };
            list.Add(new { type = "image", data = (object)new { file } });
            return list.ToArray();
        }

        public static object BuildReplyMessage(long messageId, string text)
        {
            return new object[]
            {
                new { type = "reply", data = new { id = messageId.ToString() } },
                new { type = "text", data = new { text } }
            };
        }

        public static object BuildAtAndTextMessage(long userId, string text)
        {
            return new object[]
            {
                new { type = "at", data = new { qq = userId.ToString() } },
                new { type = "text", data = new { text = " " + text } }
            };
        }

        public static object BuildImageMessage(string imageSource)
        {
            var file = NormalizeImageSource(imageSource);
            return new[]
            {
                new { type = "image", data = new { file } }
            };
        }

        public static object BuildTextAndImageMessage(string text, string imageSource)
        {
            var file = NormalizeImageSource(imageSource);
            return new object[]
            {
                new { type = "text", data = (object)new { text } },
                new { type = "image", data = (object)new { file } }
            };
        }

        public static object BuildAtTextAndImageMessage(long userId, string text, string imageSource)
        {
            var file = NormalizeImageSource(imageSource);
            return new object[]
            {
                new { type = "at", data = (object)new { qq = userId.ToString() } },
                new { type = "text", data = (object)new { text = " " + text } },
                new { type = "image", data = (object)new { file } }
            };
        }

        private static string NormalizeImageSource(string source)
        {
            if (source.StartsWith("http://") || source.StartsWith("https://"))
                return source;
            if (source.StartsWith("base64://"))
                return source;
            if (source.StartsWith("file:///"))
                return source;
            // Assume raw base64 data
            return "base64://" + source;
        }
    }
}
