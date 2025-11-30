using System;

namespace SigmaTau.Unity.ProjectGeneration
{
    public class SigmaTauExecutable
    {
        public string Filename { get; set; }

        public string Name { get; set; }

        public Func<string, string> GetStartArguments { get; set; }

        public string FocusCommand { get; set; }
    }
}

