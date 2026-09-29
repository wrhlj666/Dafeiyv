using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dafeiyv
{
    public class DeepSeekService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private string _systemPrompt;
        private bool _enableMaxReplyLength;
        private int _maxReplyLength = 100;
        private bool _enableThinking = true;   // 主聊天是否开启深度思考（设置中可关）
        private string _longTermMemoryBlock = "";   // 长期记忆片段（由 MainWindow 提供，随提示词发出）
        private const string ApiUrl = "https://api.deepseek.com/v1/chat/completions";
        private const string BalanceUrl = "https://api.deepseek.com/user/balance";

        // ✅ 情绪标签列表（用于强制追加）
        private const string EmotionList = "大笑/开心/哭/难过/比心/爆米花/紧张/慌乱/晕/赞同/反对/期待/平静/害怕/偷看/害羞/生气";

        public DeepSeekService(string apiKey, string? systemPrompt = null)
        {
            _apiKey = apiKey;
            _systemPrompt = systemPrompt ?? "鲸娘DeepSeek,有鲸尾,爱吃白饭,聪明懒,傲娇甜,听主人的,不爱被说胖";

            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };   // 推理模型思考较慢，放宽超时
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public void UpdateSystemPrompt(string newPrompt)
        {
            _systemPrompt = newPrompt;
        }

        /// <summary>当前用户自拟提示词（供数独等独立场景复用同一套性格/说话风格）</summary>
        public string CurrentSystemPrompt => _systemPrompt;

        /// <summary>设置是否限制最大回复字数（自定义时提示词会追加“回复x字以内”）</summary>
        public void SetMaxReplyLength(bool enabled, int maxLength)
        {
            _enableMaxReplyLength = enabled;
            _maxReplyLength = Math.Max(1, maxLength);
        }

        /// <summary>设置主聊天是否开启深度思考（关闭后回复快、省 token，但少一点"深思熟虑"的性格）</summary>
        public void SetThinking(bool enabled)
        {
            _enableThinking = enabled;
        }

        /// <summary>设置随提示词发出的长期记忆片段（为空表示不启用）</summary>
        public void SetLongTermMemory(string block)
        {
            _longTermMemoryBlock = block ?? "";
        }

        /// <summary>余额查询结果：Short 用于状态小字（如 ¥110.00），Full 用于气泡详情</summary>
        public sealed record BalanceResult(bool Ok, string Short, string Full);

        /// <summary>
        /// 查询 DeepSeek 账号余额（GET /user/balance）。
        /// </summary>
        public async Task<BalanceResult> GetBalanceAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(BalanceUrl);
                string json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return new BalanceResult(false, "查询失败", $"查询余额失败：{response.StatusCode}");

                return ParseBalance(json);
            }
            catch (Exception ex)
            {
                return new BalanceResult(false, "查询失败", $"查询余额出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 解析 /user/balance 的响应体（独立出来便于自测）。
        /// 形如：{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00",
        ///        "granted_balance":"10.00","topped_up_balance":"100.00"}]}
        /// </summary>
        public static BalanceResult ParseBalance(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool available = root.TryGetProperty("is_available", out var av)
                    && av.ValueKind == JsonValueKind.True;

                if (!root.TryGetProperty("balance_infos", out var infos)
                    || infos.ValueKind != JsonValueKind.Array
                    || infos.GetArrayLength() == 0)
                {
                    return new BalanceResult(false, "无数据", "查询余额失败：返回内容为空");
                }

                var lines = new List<string>();
                string shortText = "无数据";
                bool first = true;

                foreach (var info in infos.EnumerateArray())
                {
                    string currency = GetJsonString(info, "currency");
                    string total = GetJsonString(info, "total_balance");
                    string granted = GetJsonString(info, "granted_balance");
                    string toppedUp = GetJsonString(info, "topped_up_balance");
                    string symbol = currency == "USD" ? "$" : "¥";
                    lines.Add($"{symbol}{total}（充值 {symbol}{toppedUp} + 赠金 {symbol}{granted}）");
                    if (first)
                    {
                        shortText = $"{symbol}{total}";
                        first = false;
                    }
                }

                string head = available ? "💰 余额" : "⚠️ 余额不足";
                return new BalanceResult(true, shortText, head + "\n" + string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                return new BalanceResult(false, "无数据", $"查询余额出错：{ex.Message}");
            }
        }

        private static string GetJsonString(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
                if (el.ValueKind == JsonValueKind.Number) return el.ToString();
            }
            return "";
        }

        public (string emotion, string message) ParseEmotion(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return ("平静", reply);

            if (reply.StartsWith("[") && reply.Contains("]"))
            {
                int endIndex = reply.IndexOf(']');
                string emotion = reply.Substring(1, endIndex - 1).Trim();
                string message = reply.Substring(endIndex + 1).Trim();
                return (emotion, message);
            }

            return ("平静", reply);
        }

        public async Task<string> SendMessageAsync(string userMessage, List<(string role, string content)>? history = null)
        {
            System.Diagnostics.Debug.WriteLine($"📝 当前 SystemPrompt: {_systemPrompt}");
            var messages = new List<object>();

            // ✅ 强制追加情绪格式（确保表情触发）
            string fullSystemPrompt = _systemPrompt + $" 回复必须严格按照格式:[情绪]内容,情绪只能是以下之一:{EmotionList}。";
            if (_enableMaxReplyLength)
            {
                fullSystemPrompt += $"回复{_maxReplyLength}字以内。";
            }
            if (!string.IsNullOrWhiteSpace(_longTermMemoryBlock))
            {
                fullSystemPrompt += "\n" + _longTermMemoryBlock;
            }

            messages.Add(new { role = "system", content = fullSystemPrompt });

            // 历史对话（如果有）
            if (history != null)
            {
                foreach (var entry in history)
                {
                    messages.Add(new { role = entry.role, content = entry.content });
                }
            }

            // 当前用户消息
            messages.Add(new { role = "user", content = userMessage });

            // 主聊天：思考开关由设置控制；开启时保留大预算供深度思考
            return await PostChatAsync(messages, maxTokens: _enableThinking ? 8000 : 1500, enableThinking: _enableThinking);
        }

        /// <summary>
        /// 使用自定义系统提示词发送消息（不附带桌宠的情绪格式、长期记忆）。
        /// 供数独等独立场景使用，避免污染桌宠记忆。
        /// </summary>
        public async Task<string> SendMessageAsync(string userMessage, string customSystemPrompt,
            List<(string role, string content)>? history = null)
        {
            var messages = new List<object>
            {
                new { role = "system", content = customSystemPrompt }
            };
            // 可选的历史对话（数独场景只传最近一轮一问一答）
            if (history != null)
            {
                foreach (var entry in history)
                    messages.Add(new { role = entry.role, content = entry.content });
            }
            messages.Add(new { role = "user", content = userMessage });
            // 数独场景：AI 已拿到答案，只需组织语言。禁用思考模式 = 秒回 + 极省 token
            return await PostChatAsync(messages, maxTokens: 1500, enableThinking: false);
        }

        private async Task<string> PostChatAsync(List<object> messages, int maxTokens = 8000, bool enableThinking = true, int attempt = 0)
        {
            try
            {
                var requestBody = new
                {
                    model = "deepseek-flash",
                    messages = messages,
                    // 推理模型思考模式开关：关闭后不再输出 reasoning_content，回复快、省 token。
                    thinking = new { type = enableThinking ? "enabled" : "disabled" },
                    // 推理模型：思考过程与最终回复共用 max_tokens 预算。
                    // 预算不足时思考会吃光额度导致 content 为空；数独场景已附答案无需深想，用较小预算更快。
                    max_tokens = maxTokens,
                    temperature = 0.7
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ApiUrl, content);
                var jsonResponse = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(jsonResponse);

                    System.Diagnostics.Debug.WriteLine($"完整响应：{jsonResponse}");

                    // 只取最终回复 content；reasoning_content 是思考过程，绝不展示给用户。
                    // content 为空说明思考占满了 max_tokens，重试一次；仍空则报错带原始响应。
                    string? reply = null;
                    if (doc.RootElement.TryGetProperty("choices", out var choicesEl) &&
                        choicesEl.GetArrayLength() > 0 &&
                        choicesEl[0].TryGetProperty("message", out var msgEl) &&
                        msgEl.TryGetProperty("content", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                    {
                        reply = cEl.GetString();
                    }

                    System.Diagnostics.Debug.WriteLine($"提取的回复：'{reply}'");

                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        if (attempt == 0)
                            return await PostChatAsync(messages, maxTokens, enableThinking, 1);
                        return $"（模型未生成最终回复，可能是思考过长，原始响应：{Truncate(jsonResponse, 600)}）";
                    }

                    return reply;
                }
                else
                {
                    return $"API 出错：{response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"网络错误：{ex.Message}";
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}