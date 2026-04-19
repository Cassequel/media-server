using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rockflix.API.Data;
using Rockflix.API.Services;

namespace Rockflix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController(AppDbContext db, MediaScannerService scanner) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> ScanMedia()
    {
        await scanner.ScanAsync();
        return Ok(new { message = "Media scan complete." });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await db.Users
            .Select(u => new { u.Id, u.Username, u.Email, u.IsAdmin, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.IsAdmin) return BadRequest(new { message = "Cannot delete an admin user." });

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("users/{id}/admin")]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.IsAdmin = !user.IsAdmin;
        await db.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.IsAdmin });
    }
}
