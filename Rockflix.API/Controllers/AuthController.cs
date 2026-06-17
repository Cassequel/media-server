using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.DTOs.Auth;
using Rockflix.API.Models;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext db, TokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            return Conflict(new { message = "Email already in use." });

        if (await db.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict(new { message = "Username already taken." });

        var isFirstUser = !await db.Users.AnyAsync();

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsAdmin = isFirstUser,
            IsActive = isFirstUser
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        if (!isFirstUser)
            return StatusCode(202, new { pending = true, message = "Your account is pending approval by an administrator." });

        return Ok(new AuthResponse
        {
            Token = tokenService.GenerateToken(user),
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        if (!user.IsActive)
            return StatusCode(403, new { message = "Your account is pending approval by an administrator." });

        return Ok(new AuthResponse
        {
            Token = tokenService.GenerateToken(user),
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin
        });
    }
}
