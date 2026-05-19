namespace RagAMuffin.Models
{
    public class AppSettings
    {
        public string  LlmModel      { get; set; } = "llama3.2";
        public string? SystemPrompt  { get; set; }
        public bool    NoRetrieval   { get; set; } = false;
        public List<QuickPrompt> QuickPrompts { get; set; } = [];
    }

    public class QuickPrompt
    {
        public string Label    { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
    }
}
