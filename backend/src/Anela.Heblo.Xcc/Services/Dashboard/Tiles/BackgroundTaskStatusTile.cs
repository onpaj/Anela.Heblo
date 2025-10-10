using Anela.Heblo.Xcc.Services.BackgroundRefresh;

namespace Anela.Heblo.Xcc.Services.Dashboard.Tiles;

public class BackgroundTaskStatusTile : ITile
{
    private readonly IBackgroundRefreshTaskRegistry _taskRegistry;
    
    // Self-describing metadata
    public string Title => "Stav background tasků";
    public string Description => "Aktuální stav všech background úloh";
    public TileSize Size => TileSize.Small;
    public TileCategory Category => TileCategory.System;
    public bool DefaultEnabled => true;
    public bool AutoShow => true; // System tile - auto-show
    public Type ComponentType => typeof(object); // Frontend component type not needed for backend
    public string[] RequiredPermissions => Array.Empty<string>();
    
    public BackgroundTaskStatusTile(IBackgroundRefreshTaskRegistry taskRegistry)
    {
        _taskRegistry = taskRegistry;
    }
    
    public Task<object> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        var registeredTasks = _taskRegistry.GetRegisteredTasks();
        
        // Get latest execution status for each task
        var latestExecutions = registeredTasks.Select(task =>
        {
            var lastExecution = _taskRegistry.GetLastExecution(task.TaskId);
            return new
            {
                task.TaskId,
                Status = lastExecution?.Status ?? RefreshTaskExecutionStatus.Failed // Default to Failed if never executed
            };
        }).ToList();
        
        var completed = latestExecutions.Count(t => t.Status == RefreshTaskExecutionStatus.Completed);
        
        var result = new
        {
            Completed = completed,
            Total = registeredTasks.Count,
            Status = $"{completed}/{registeredTasks.Count}"
        };
        
        return Task.FromResult((object)result);
    }
}