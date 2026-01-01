using System.Security.Claims;

namespace Scissortail.MvcExample;

public static class ClaimsPrincipalExtensions {
    public static string? Name(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Name);
    public static string? Email(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Email);
    public static string? ProfilePictureUrl(this ClaimsPrincipal principal) => principal.FindFirstValue("picture");
}
