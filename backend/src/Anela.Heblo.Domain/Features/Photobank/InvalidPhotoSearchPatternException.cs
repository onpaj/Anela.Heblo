namespace Anela.Heblo.Domain.Features.Photobank
{
    public class InvalidPhotoSearchPatternException : Exception
    {
        public string Pattern { get; }

        public InvalidPhotoSearchPatternException(string pattern)
            : base($"Invalid search pattern: {pattern}")
        {
            Pattern = pattern;
        }
    }
}
