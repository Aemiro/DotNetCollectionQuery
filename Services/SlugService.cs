namespace CollectionQuery.Services
{

    public static class SlugService
    {
        public static string Generate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Guid.NewGuid().ToString("n");
            var slug = new string(text
                .Trim()
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray());
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Trim('-');
        }
    }

}
