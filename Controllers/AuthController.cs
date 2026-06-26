using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using backend.Database;
using backend.Models;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // POST: api/auth/register
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Tên đăng nhập và mật khẩu không được để trống.");

            if (req.Username.Length < 3)
                return BadRequest("Tên đăng nhập phải có ít nhất 3 ký tự.");

            if (req.Password.Length < 6)
                return BadRequest("Mật khẩu phải có ít nhất 6 ký tự.");

            var exists = await _context.Users.AnyAsync(u => u.Username == req.Username.ToLower().Trim());
            if (exists)
                return Conflict("Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.");

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Username = req.Username.ToLower().Trim(),
                DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? req.Username.Trim() : req.DisplayName.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                Role = "user",
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đăng ký thành công! Bạn có thể đăng nhập ngay bây giờ." });
        }

        // POST: api/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Vui lòng nhập tên đăng nhập và mật khẩu.");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == req.Username.ToLower().Trim());

            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
                return Unauthorized("Tên đăng nhập hoặc mật khẩu không đúng.");

            var token = GenerateJwt(user);

            return Ok(new
            {
                token,
                user = new { id = user.Id, username = user.Username, displayName = user.DisplayName, role = user.Role }
            });
        }

        // GET: api/auth/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var userId = GetUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            return Ok(new { id = user.Id, username = user.Username, displayName = user.DisplayName, role = user.Role });
        }

        // PUT: api/auth/change-password
        [HttpPut("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var userId = GetUserId();
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
                return BadRequest("Mật khẩu cũ không đúng.");

            if (req.NewPassword.Length < 6)
                return BadRequest("Mật khẩu mới phải có ít nhất 6 ký tự.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

        // ── Helpers ───────────────────────────────────────────────
        private string GenerateJwt(AppUser user)
        {
            var jwtKey = _config["Jwt:Key"] ?? "antigravity-lang-secret-key-2024-secure";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("displayName", user.DisplayName),
            };

            var token = new JwtSecurityToken(
                issuer: "antigravity-lang",
                audience: "antigravity-lang",
                claims: claims,
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public Guid GetUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
