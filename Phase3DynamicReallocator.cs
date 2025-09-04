using System.Text.Json;
using DataModel;

namespace DynamicReallocation
{
    public record DynamicEvent(string Type, string? Node = null, DataModel.Task? Task = null);

    public record Phase3Input(
        Dictionary<string, (string node, int start_time)> PreviousSchedule,
        List<DynamicEvent> Events,
        Dictionary<string, Dictionary<int, int>> NodeCapacityUpdate
    );

    public record Phase3Output(
        Dictionary<string, (string node, int start_time)> UpdatedSchedule,
        List<string> ReassignedTasks,
        List<string> FailedTasks,
        int TotalCost,
        int ChangePenalty
    );

    public class DynamicReallocator
    {
        private Dictionary<string, (string node, int start_time)> schedule;
        private List<DataModel.Task> tasks;
        private List<Node> nodes;
        private Dictionary<string, int> taskDurations;
        private Dictionary<string, Dictionary<int, int>> cpuPerTime;
        private Dictionary<string, Dictionary<int, int>> ramPerTime;
        private int totalCostPhase1;
        private int changePenalty;

        public DynamicReallocator(
            List<DataModel.Task> tasks,
            List<Node> nodes,
            Dictionary<string, int> taskDurations,
            Dictionary<string, Dictionary<int, int>> cpuPerTime,
            Dictionary<string, Dictionary<int, int>> ramPerTime,
            int totalCostPhase1,
            Dictionary<string, (string node, int start_time)> previousSchedule
        )
        {
            this.tasks = tasks.ToList();
            this.nodes = nodes.ToList();
            this.taskDurations = new Dictionary<string, int>(taskDurations);
            this.cpuPerTime = DeepCopy(cpuPerTime);
            this.ramPerTime = DeepCopy(ramPerTime);
            this.totalCostPhase1 = totalCostPhase1;
            this.schedule = new Dictionary<string, (string node, int start_time)>(previousSchedule);
            this.changePenalty = 0;
        }

        public Phase3Output ProcessEvents(List<DynamicEvent> events)
        {
            var reassignedTasks = new List<string>();
            var failedTasks = new List<string>();

            foreach (var ev in events)
            {
                switch (ev.Type.ToLower())
                {
                    case "node_failure":
                        if (ev.Node != null)
                        {
                            var affectedTasks = schedule
                                .Where(kv => kv.Value.node == ev.Node)
                                .Select(kv => kv.Key)
                                .ToList();

                            foreach (var tId in affectedTasks)
                            {
                                schedule.Remove(tId);
                                reassignedTasks.Add(tId);
                                failedTasks.Add(tId);
                            }

                            cpuPerTime.Remove(ev.Node);
                            ramPerTime.Remove(ev.Node);
                        }
                        break;

                    case "new_task":
                        if (ev.Task != null)
                        {
                            tasks.Add(ev.Task);
                            taskDurations[ev.Task.Id] = 1;
                            reassignedTasks.Add(ev.Task.Id);
                        }
                        break;
                }
            }

            foreach (var tId in reassignedTasks.ToList())
            {
                var task = tasks.FirstOrDefault(t => t.Id == tId);
                if (task == null)
                {
                    failedTasks.Add(tId);
                    continue;
                }

                bool assigned = false;
                foreach (var node in nodes)
                {
                    int startTime = 0;
                    int duration = taskDurations.TryGetValue(tId, out var d) ? d : 1;

                    while (true)
                    {
                        if (
                            CanFit(node.Id, startTime, duration, task.CpuRequired, task.RamRequired)
                        )
                        {
                            Reserve(
                                node.Id,
                                startTime,
                                duration,
                                task.CpuRequired,
                                task.RamRequired
                            );
                            schedule[tId] = (node.Id, startTime);
                            assigned = true;
                            changePenalty++;
                            failedTasks.Remove(tId);
                            break;
                        }
                        startTime++;
                        if (startTime > task.deadline - duration)
                            break;
                    }

                    if (assigned)
                        break;
                }

                if (!assigned && !failedTasks.Contains(tId))
                    failedTasks.Add(tId);
            }

            int totalCost = totalCostPhase1 + changePenalty;
            return new Phase3Output(
                schedule,
                reassignedTasks,
                failedTasks,
                totalCost,
                changePenalty
            );
        }

        private bool CanFit(string nodeId, int start, int dur, int cpuReq, int ramReq)
        {
            if (!cpuPerTime.ContainsKey(nodeId))
                return false;
            for (int t = start; t < start + dur; t++)
            {
                if (!cpuPerTime[nodeId].TryGetValue(t, out var cpuAvail) || cpuAvail < cpuReq)
                    return false;
                if (
                    !ramPerTime.ContainsKey(nodeId)
                    || !ramPerTime[nodeId].TryGetValue(t, out var ramAvail)
                    || ramAvail < ramReq
                )
                    return false;
            }
            return true;
        }

        private void Reserve(string nodeId, int start, int dur, int cpuReq, int ramReq)
        {
            for (int t = start; t < start + dur; t++)
            {
                cpuPerTime[nodeId][t] -= cpuReq;
                ramPerTime[nodeId][t] -= ramReq;
            }
        }

        private static Dictionary<string, Dictionary<int, int>> DeepCopy(
            Dictionary<string, Dictionary<int, int>> src
        )
        {
            var dst = new Dictionary<string, Dictionary<int, int>>();
            foreach (var (k, v) in src)
                dst[k] = v.ToDictionary(x => x.Key, x => x.Value);
            return dst;
        }

        public string ToJson(Phase3Output output)
        {
            return JsonSerializer.Serialize(
                output,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
    }
}
