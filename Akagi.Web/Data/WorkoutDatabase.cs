using Akagi.Web.Models.Workouts;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Data;

public interface IWorkoutDatabase : IDatabase<WorkoutRoutine>;

public class WorkoutDatabase : Database<WorkoutRoutine>, IWorkoutDatabase
{
    public WorkoutDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "workouts")
    {
    }
}
