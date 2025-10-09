using System.Security.Claims;
using Anela.Heblo.API.Controllers.Dashboard;
using Anela.Heblo.Xcc.Domain;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : BaseApiController
{
    private readonly ITileRegistry _tileRegistry;
    private readonly IDashboardService _dashboardService;

    public DashboardController(ITileRegistry tileRegistry, IDashboardService dashboardService)
    {
        _tileRegistry = tileRegistry;
        _dashboardService = dashboardService;
    }

    [HttpGet("tiles")]
    public async Task<ActionResult<IEnumerable<DashboardTileDto>>> GetAvailableTiles()
    {
        var tiles = _tileRegistry.GetAvailableTiles();
        
        var result = tiles.Select(t => new DashboardTileDto
        {
            TileId = t.GetTileId(),
            Title = t.Title,
            Description = t.Description,
            Size = t.Size.ToString(),
            Category = t.Category.ToString(),
            DefaultEnabled = t.DefaultEnabled,
            AutoShow = t.AutoShow,
            RequiredPermissions = t.RequiredPermissions
        });

        return Ok(result);
    }

    [HttpGet("settings")]
    public async Task<ActionResult<UserDashboardSettingsDto>> GetUserSettings()
    {
        var userId = GetCurrentUserId();
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        var result = new UserDashboardSettingsDto
        {
            Tiles = settings.Tiles.Select(t => new UserDashboardTileDto
            {
                TileId = t.TileId,
                IsVisible = t.IsVisible,
                DisplayOrder = t.DisplayOrder
            }).ToArray(),
            LastModified = settings.LastModified
        };

        return Ok(result);
    }

    [HttpPost("settings")]
    public async Task<ActionResult> SaveUserSettings([FromBody] SaveDashboardSettingsRequest request)
    {
        var userId = GetCurrentUserId();
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        // Update tile settings
        foreach (var tileDto in request.Tiles)
        {
            var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileDto.TileId);
            if (existingTile != null)
            {
                existingTile.IsVisible = tileDto.IsVisible;
                existingTile.DisplayOrder = tileDto.DisplayOrder;
                existingTile.LastModified = DateTime.UtcNow;
            }
            else
            {
                // Add new tile
                settings.Tiles.Add(new UserDashboardTile
                {
                    UserId = userId,
                    TileId = tileDto.TileId,
                    IsVisible = tileDto.IsVisible,
                    DisplayOrder = tileDto.DisplayOrder,
                    LastModified = DateTime.UtcNow,
                    DashboardSettings = settings
                });
            }
        }
        
        settings.LastModified = DateTime.UtcNow;
        await _dashboardService.SaveUserSettingsAsync(userId, settings);
        
        return Ok();
    }

    [HttpGet("data")]
    public async Task<ActionResult<IEnumerable<DashboardTileDto>>> GetTileData()
    {
        var userId = GetCurrentUserId();
        var tileData = await _dashboardService.GetTileDataAsync(userId);
        
        var result = tileData.Select(td => new DashboardTileDto
        {
            TileId = td.TileId,
            Title = td.Title,
            Description = td.Description,
            Size = td.Size.ToString(),
            Category = td.Category.ToString(),
            DefaultEnabled = td.DefaultEnabled,
            AutoShow = td.AutoShow,
            RequiredPermissions = td.RequiredPermissions,
            Data = td.Data
        });

        return Ok(result);
    }

    [HttpPost("tiles/{tileId}/enable")]
    public async Task<ActionResult> EnableTile(string tileId)
    {
        var userId = GetCurrentUserId();
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = true;
            existingTile.LastModified = DateTime.UtcNow;
        }
        else
        {
            // Add new tile with next display order
            var maxOrder = settings.Tiles.Any() ? settings.Tiles.Max(t => t.DisplayOrder) : -1;
            settings.Tiles.Add(new UserDashboardTile
            {
                UserId = userId,
                TileId = tileId,
                IsVisible = true,
                DisplayOrder = maxOrder + 1,
                LastModified = DateTime.UtcNow,
                DashboardSettings = settings
            });
        }
        
        settings.LastModified = DateTime.UtcNow;
        await _dashboardService.SaveUserSettingsAsync(userId, settings);

        return Ok();
    }

    [HttpPost("tiles/{tileId}/disable")]
    public async Task<ActionResult> DisableTile(string tileId)
    {
        var userId = GetCurrentUserId();
        var settings = await _dashboardService.GetUserSettingsAsync(userId);
        
        var existingTile = settings.Tiles.FirstOrDefault(t => t.TileId == tileId);
        if (existingTile != null)
        {
            existingTile.IsVisible = false;
            existingTile.LastModified = DateTime.UtcNow;
            settings.LastModified = DateTime.UtcNow;
            
            await _dashboardService.SaveUserSettingsAsync(userId, settings);
        }

        return Ok();
    }

    private string GetCurrentUserId()
    {
        // Get user ID from authentication claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    ?? User.FindFirst("sub")?.Value 
                    ?? User.FindFirst("oid")?.Value
                    ?? "anonymous";
        
        return userId;
    }
}