using System.Collections.Generic;

namespace DevTools.Apps
{
    public class AppOptions
    {
        public bool Save { get; set; }
        public bool Update { get; set; }
        public bool List { get; set; }
        public List<AppOptionVariant> Variants { get; set; }

        public AppOptions()
        {
            Variants = new List<AppOptionVariant>();
        }

        public void AddVariant(string appName, string variantName)
        {
            Variants.Add(new AppOptionVariant() {AppName = appName, VariantName = variantName});
        }
    }
}