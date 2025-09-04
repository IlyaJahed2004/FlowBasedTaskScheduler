using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DataModel;

namespace LocalNodeScheduling
{
    public record LocalTask(string Id, int Cpu, int Ram, int Duration, int Deadline);

    public record Phase4Input(
        string NodeId,
        List<LocalTask> AssignedTasks,
        Dictionary<int, Dictionary<string, int>> ResourcePerTime, // time -> {"cpu": x}
        List<int> TimeSlots
    );

    public record TaskExecutionInfo(int? StartTime, bool MeetsDeadline);

    public record Phase4Output(
        Dictionary<string, TaskExecutionInfo> ExecutionSchedule,
        int TotalIdleTime,
        int PenaltyCost
    );

    public class LocalSchedulerDP
    {
        private readonly Phase4Input input;
        private readonly Dictionary<int, int> cpuPerTime; // simplified for CPU only

        public LocalSchedulerDP(Phase4Input input)
        {
            this.input = input;
            cpuPerTime = new Dictionary<int, int>();
            foreach (var t in input.TimeSlots)
            {
                cpuPerTime[t] =
                    input.ResourcePerTime.TryGetValue(t, out var res) && res.ContainsKey("cpu")
                        ? res["cpu"]
                        : 0;
            }
        }

        public Phase4Output Schedule()
        {
            var tasks = input.AssignedTasks.OrderBy(t => t.Deadline).ToList(); // Earliest deadline first
            var schedule = new Dictionary<string, TaskExecutionInfo>();
            int penalty = 0;
            int totalIdle = 0;

            foreach (var task in tasks)
            {
                bool scheduled = false;
                for (int t = 0; t <= input.TimeSlots.Max() - task.Duration + 1; t++)
                {
                    if (CanFit(t, task.Duration, task.Cpu))
                    {
                        Reserve(t, task.Duration, task.Cpu);
                        schedule[task.Id] = new TaskExecutionInfo(
                            t,
                            t + task.Duration <= task.Deadline
                        );
                        if (t + task.Duration > task.Deadline)
                            penalty++;
                        scheduled = true;
                        break;
                    }
                }

                if (!scheduled)
                {
                    // task cannot be scheduled, mark as failed
                    schedule[task.Id] = new TaskExecutionInfo(null, false);
                    penalty++;
                }
            }

            totalIdle = cpuPerTime.Values.Sum(c => c); // remaining CPU = idle
            return new Phase4Output(schedule, totalIdle, penalty);
        }

        private bool CanFit(int start, int duration, int cpuReq)
        {
            for (int t = start; t < start + duration; t++)
            {
                if (!cpuPerTime.ContainsKey(t) || cpuPerTime[t] < cpuReq)
                    return false;
            }
            return true;
        }

        private void Reserve(int start, int duration, int cpuReq)
        {
            for (int t = start; t < start + duration; t++)
                cpuPerTime[t] -= cpuReq;
        }

        public string ToJson(Phase4Output output)
        {
            return JsonSerializer.Serialize(
                output,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
    }
}
