using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ILocalizationService
    {
        string DefaultCulture { get; }

        string GetString(string key);
        string GetString(string key, string culture);
        string GetString(string key, string culture, params object[] parameters);
    }

    internal class LocalizationService : ILocalizationService
    {
        public LocalizationService(IConfiguration config)
        {
            IConfigurationSection localizationSection = config.GetSection("Localization");
            DefaultCulture = localizationSection.GetValue<string>("DefaultCulture")
                ?? throw new ArgumentException("Specified configuration section does not contain a default culture entry.");
            var stringSection = localizationSection.GetSection("Strings");
            foreach (IConfigurationSection culture in stringSection.GetChildren())
            {
                Dictionary<string, string> strings = new Dictionary<string, string>();
                foreach (IConfigurationSection str in culture.GetChildren())
                {
                    strings.Add(str.Key, str.Value);
                }
                store.Add(culture.Key, strings);
            }
            if (!store.ContainsKey(DefaultCulture))
            {
                throw new ArgumentException("Specified default culture does not exist in provided strings.");
            }
        }

        public string DefaultCulture { get; }

        public string GetString(string key) => GetString(key, DefaultCulture);
        public string GetString(string key, string culture)
        {
            if (!store.ContainsKey(culture))
            {
                throw new ArgumentException("Specified culture does not exist in provided strings.");
            }
            string str = store[culture].GetValueOrDefault(key) ?? store[DefaultCulture].GetValueOrDefault(key);
            if (str == null)
            {
                throw new ArgumentException("String for specified key does not exist for provided culture or the default culture.");
            }
            return str;
        }
        public string GetString(string key, string culture, params object[] parameters)
        {
            string pattern = GetString(key, culture);
            return string.Format(pattern, parameters);
        }

        private readonly Dictionary<string, Dictionary<string, string>> store = new Dictionary<string, Dictionary<string, string>>();
    }
}
