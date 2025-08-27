namespace Downloadr.Cli.Constants;

public static class UiLayout
{
    public const int MaxTableWidth = 120;
    public const int NameColumnMin = 12;
    public const int NameColumnMax = 70;
    public const int ApproxOtherColumnsWidth = 42;
}

public static class UiColours
{
    public const string KeyDark = "deepskyblue1";
    public const string KeyLight = "blue";
    public const string TextDark = "grey35";
    public const string TextLight = "grey50";

    public static class Dark
    {
        public const string Running = "green1";
        public const string Queued = "grey46";
        public const string Paused = "grey46";
        public const string Completed = "grey39";
        public const string Cancelled = "red3 dim";
        public const string Failed = "gold3";
    }

    public static class Light
    {
        public const string Running = "green3";
        public const string Queued = "grey54";
        public const string Paused = "grey54";
        public const string Completed = "grey60";
        public const string Cancelled = "red3";
        public const string Failed = "darkorange3";
    }
}


