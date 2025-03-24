namespace SigmaTau.Unity.ProjectGeneration
{
    public class SigmaTauProjectGenerationOptions
    {
        public bool IncludePackages { get; set; }

        public string[] Analyzers { get; set; }

        public string ProjectTypeGuid { get; set; }

        public string[] CapabilitiesToRemove { get; set; }
    }
}
