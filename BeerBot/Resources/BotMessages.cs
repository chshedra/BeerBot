namespace BeerBot.Resources;

/// <summary>
/// All user-facing Telegram text lives here, grouped by feature.
/// Literal text is a const; parameterized text is a static helper so the
/// interpolation stays next to the wording. Log templates stay inline at the
/// call site — they are structured-logging messages, not UI.
/// </summary>
public static class BotMessages
{
    /// <summary>Text for the /beertime round-start flow.</summary>
    public static class Beertime
    {
        public const string NotLinked =
            "Ты пока не привязан к группе. Добавь меня в группу и нажми кнопку "
            + "регистрации под моим приветствием. 🍺";

        public const string RoundInProgress =
            "Раунд уже идёт! Загляни в личку, чтобы выбрать слоты, или жди итогов в чате.";

        public const string JoinButton = "🍺 Участвовать";

        public static string GroupAnnouncement(string name, int deadlineHours) =>
            $"🍺 *Beertime!* {name} хочет собрать всех. "
            + "Нажми Участвовать, чтобы выбрать своё время. Как только все ответят, я предложу варианты. "
            + $"(или подведу итог через {deadlineHours}ч).";
    }

    /// <summary>Text for the inline-button availability wizard.</summary>
    public static class Wizard
    {
        public const string PickDay = "🍺 Когда сможешь? Выбери день:";
        public const string RoundClosed = "Этот раунд уже закрыт 🍺";
        public const string SelectAtLeastOne = "Сначала выбери хотя бы один слот 🍺";
        public const string Saved = "Сохранено 🍺";

        public const string DoneButton = "✅ Готово";
        public const string BackToDaysButton = "⬅ Дни";

        public static string PickHours(string day) =>
            $"🍺 {day} — выбери удобные часы (можно несколько):";

        public static string Submitted(int slotCount) =>
            $"Готово! Записал {slotCount} слот(ов). "
            + "Скину в чат лучшее время, когда все ответят. 🍺";

        public static string Today(string label) => $"Сегодня ({label})";

        public static string Tomorrow(string label) => $"Завтра ({label})";
    }

    /// <summary>Text for the suggestion and poll posted to the group.</summary>
    public static class Suggest
    {
        public const string NoActiveRound = "Нет активного раунда. Начни в личке: /beertime";

        public const string NobodyPicked = "Пока никто не выбрал время — нечего предложить. 😔";

        public const string NoOverlap = "Не нашёл общего окна — ни один слот не пересёкся. 😔";

        public const string BestTimeHeader = "🍺 Лучшее время для встречи:";

        public const string PollQuestion = "🍺 Когда встречаемся?";

        public const string PollNoneOption = "Ни один не подходит";

        public static string SlotLine(string slot, int memberCount, int total) =>
            $"• {slot} — свободны {memberCount}/{total}";
    }

    /// <summary>Text for the /status report.</summary>
    public static class Status
    {
        public const string NoActiveRequest =
            "No active meeting request. Start one with /beertime!";

        public const string EveryoneReplied = "\nEveryone has replied! Generating suggestion...";

        public static string Header(DateTime deadline) =>
            $"📊 *Meeting request status* (deadline: {deadline:ddd HH:mm} UTC)";

        public static string MemberReplied(string name) => $"✅ {name}";

        public static string MemberWaiting(string name) => $"⏳ {name}";

        public static string WaitingForMore(int count) => $"\nWaiting for {count} more.";
    }

    /// <summary>Text for registration, deep-link onboarding, and the group welcome.</summary>
    public static class Registration
    {
        public const string DmToStart = "Напиши мне /beertime в личку, чтобы начать раунд 🍺";

        public const string UseButtons =
            "Время выбирается кнопками 🍺 Напиши /beertime, чтобы начать раунд, "
            + "или жди приглашения, когда его начнёт кто-то из группы.";

        public const string OpenFromGroup =
            "Привет! Открой меня по кнопке из группы, чтобы я привязал тебя к ней. 🍺";

        public const string LinkedNoRound =
            "Готово! 🍺 Напиши /beertime, чтобы начать раунд, или жди приглашения от группы.";

        public const string AlreadySubmitted = "Спасибо, твоё время уже записано на этот раунд! 🍺";

        public static string Welcome(string deepLink) =>
            "🍺 *Я пивной бот!*\n\n"
            + "Помогаю найти время и бахнуть пивка.\n\n"
            + "Как это работает:\n"
            + "1. Каждый жмёт кнопку ниже и стартует меня в личке.\n"
            + "2. Любой пишет мне /beertime в личку, чтобы начать раунд.\n"
            + "3. Все выбирают своё время кнопками — я нахожу пересечение и кидаю опрос сюда.\n\n"
            + $"Регистрация: [нажми тут]({deepLink})";
    }
}
