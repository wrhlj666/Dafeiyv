namespace Dafeiyv
{
    public class AppConfig
    {
        public string ApiKey { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public double PetScale { get; set; } = 1.0;              // 宠物缩放（0.5 ~ 2.0）
        public int IdleActionIntervalSeconds { get; set; } = 10;  // 随机动作触发间隔（秒，最低 10）
        public string Welcome1 { get; set; } = "大肥鱼来啦！";
        public string Welcome2 { get; set; } = "今天想聊什么？";
        public string Welcome3 { get; set; } = "点我聊天～";
        public string Welcome4 { get; set; } = "我在这里哦！";
        public int MaxHistoryRounds { get; set; } = 10;           // 最大记忆轮数（10/5/0，0=无记忆）
        public bool EnableMaxReplyLength { get; set; } = false;   // 是否限制最大回复字数
        public int MaxReplyLength { get; set; } = 100;            // 自定义最大回复字数
        public bool EnableThinking { get; set; } = true;          // 主聊天是否开启深度思考（关=快/省）
        public bool EnableLongTermMemory { get; set; } = false;   // 是否启用长期记忆（默认关闭，开启时提示 token 消耗）
        public int MemoryEntryMaxChars { get; set; } = 40;        // 每条长期记忆最大字数
        public bool EnableProactiveSpeak { get; set; } = true;    // 是否启用主动说话
        public int ProactiveSpeakIntervalSeconds { get; set; } = 300;   // 主动说话间隔（秒，最低 10）
        public bool EnableRoam { get; set; } = true;              // 是否启用漫游
        public int RoamIntervalSeconds { get; set; } = 30;        // 漫游间隔（秒，最低 1）
    }
}
