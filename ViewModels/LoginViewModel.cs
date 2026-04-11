using System.ComponentModel.DataAnnotations;

namespace SecureFileHub.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Enter a valid email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // "Remember me" checkbox — optional for now
        public bool RememberMe { get; set; }
    }
}
