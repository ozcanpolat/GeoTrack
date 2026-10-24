using System.ComponentModel.DataAnnotations;

namespace GeoTrack.Web.Services;

public static class ValidationHelper
{
    /// <summary>DataAnnotations ile nesneyi doğrular; hataları alan adına göre gruplar.</summary>
    public static bool TryValidate(object model, out Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        var ok = Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        errors = results
            .SelectMany(r =>
            {
                var members = r.MemberNames.Any() ? r.MemberNames : [string.Empty];
                return members.Select(m => (Member: m, Message: r.ErrorMessage ?? "Geçersiz değer."));
            })
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray());

        return ok;
    }
}
