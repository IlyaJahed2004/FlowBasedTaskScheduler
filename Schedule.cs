using System;
using System.Collections.Generic;
using System.Linq;
using DataModel;

namespace Scheduling
{
    public record Dependency(string Before, string After);

    public record ScheduledItem(string TaskId, string NodeId, int StartTime, int Duration);

    public class Phase2Result
    {
        public bool Valid { get; init; }
        public int TotalCost { get; init; } // از فاز 1
        public Dictionary<string, (string node, int start_time)> Schedule { get; init; } = new();
        public string? Reason { get; init; } // اگر invalid بود چرا
    }

    public class TimeAwareScheduler
    {
        private readonly List<DataModel.Task> tasks;
        private readonly List<Node> nodes;
        private readonly Dictionary<string, string> assignment; // TaskId -> NodeId
        private readonly List<int> timeSlots;
        private readonly Dictionary<string, Dictionary<int, int>> cpuPerTime; // nodeId -> (t -> CPU)
        private readonly Dictionary<string, Dictionary<int, int>>? ramPerTime;
        private readonly Dictionary<string, int> duration; // TaskId -> duration
        private readonly List<Dependency> dependencies;
        private readonly int totalCostPhase1;

        public TimeAwareScheduler(
            List<DataModel.Task> tasks,
            List<Node> nodes,
            Dictionary<string, string> assignment,
            List<int> timeSlots,
            Dictionary<string, Dictionary<int, int>> cpuPerTime,
            Dictionary<string, Dictionary<int, int>>? ramPerTime,
            Dictionary<string, int> duration,
            List<Dependency> dependencies,
            int totalCostPhase1
        )
        {
            this.tasks = tasks;
            this.nodes = nodes;
            this.assignment = assignment;
            this.timeSlots = timeSlots.OrderBy(t => t).ToList();
            this.cpuPerTime = DeepCopy(cpuPerTime);
            this.ramPerTime = ramPerTime is null ? null : DeepCopy(ramPerTime);
            this.duration = duration;
            this.dependencies = dependencies;
            this.totalCostPhase1 = totalCostPhase1;
        }

        public Phase2Result Solve()
        {
            // 1) Map برای lookup سریع
            var taskById = tasks.ToDictionary(t => t.Id, t => t);
            var nodeById = nodes.ToDictionary(n => n.Id, n => n);

            // 2) Topological sort
            var topo = TopologicalOrder(taskById.Keys.ToList(), dependencies, out string? cycleErr);
            if (topo is null)
            {
                return new Phase2Result
                {
                    Valid = false,
                    TotalCost = totalCostPhase1,
                    Reason = cycleErr,
                };
            }

            var finishTime = new Dictionary<string, int>();
            var schedule = new Dictionary<string, (string node, int start_time)>();

            // 3) Greedy scheduling
            foreach (var taskId in topo)
            {
                if (!assignment.TryGetValue(taskId, out var nodeId))
                {
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason = $"Task {taskId} has no assigned node from Phase 1.",
                    };
                }

                var task = taskById[taskId];
                var d = duration.TryGetValue(taskId, out var dd) ? dd : 1;

                // earliest start = max(predecessors’ finish)
                int est = 0;
                foreach (var dep in dependencies.Where(dep => dep.After == taskId))
                {
                    if (!finishTime.TryGetValue(dep.Before, out var f))
                        return new Phase2Result
                        {
                            Valid = false,
                            TotalCost = totalCostPhase1,
                            Reason = $"Predecessor {dep.Before} of {taskId} is unscheduled.",
                        };
                    est = Math.Max(est, f);
                }

                // search start time در بازه [est .. deadline-d]
                int latestStart = task.deadline - d;
                bool placed = false;

                for (int t = est; t <= latestStart; t++)
                {
                    if (CanFit(nodeId, t, d, task.CpuRequired, task.RamRequired))
                    {
                        Reserve(nodeId, t, d, task.CpuRequired, task.RamRequired);
                        schedule[taskId] = (nodeId, t);
                        finishTime[taskId] = t + d;
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason =
                            $"Cannot schedule {taskId} on {nodeId} before deadline {task.deadline}.",
                    };
                }
            }

            return new Phase2Result
            {
                Valid = true,
                TotalCost = totalCostPhase1,
                Schedule = schedule,
            };
        }

        private bool CanFit(string nodeId, int start, int dur, int cpuReq, int ramReq)
        {
            if (!cpuPerTime.ContainsKey(nodeId))
                return false;
            for (int k = 0; k < dur; k++)
            {
                int t = start + k;
                if (!timeSlots.Contains(t))
                    return false;

                if (!cpuPerTime[nodeId].TryGetValue(t, out var cpuAvail))
                    return false;
                if (cpuAvail < cpuReq)
                    return false;

                if (ramPerTime != null)
                {
                    if (!ramPerTime.TryGetValue(nodeId, out var ramMap))
                        return false;
                    if (!ramMap.TryGetValue(t, out var ramAvail))
                        return false;
                    if (ramAvail < ramReq)
                        return false;
                }
            }
            return true;
        }

        private void Reserve(string nodeId, int start, int dur, int cpuReq, int ramReq)
        {
            for (int k = 0; k < dur; k++)
            {
                int t = start + k;
                cpuPerTime[nodeId][t] -= cpuReq;
                if (ramPerTime != null)
                {
                    ramPerTime[nodeId][t] -= ramReq;
                }
            }
        }

        private static List<string>? TopologicalOrder(
            List<string> taskIds,
            List<Dependency> deps,
            out string? error
        )
        {
            error = null;

            var adj = taskIds.ToDictionary(id => id, _ => new List<string>());
            var indeg = taskIds.ToDictionary(id => id, _ => 0);

            foreach (var d in deps)
            {
                if (!adj.ContainsKey(d.Before) || !adj.ContainsKey(d.After))
                {
                    error = $"Dependency refers to unknown task: {d.Before} -> {d.After}";
                    return null;
                }
                adj[d.Before].Add(d.After);
                indeg[d.After]++;
            }

            var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var order = new List<string>();

            while (q.Count > 0)
            {
                var u = q.Dequeue();
                order.Add(u);
                foreach (var v in adj[u])
                {
                    indeg[v]--;
                    if (indeg[v] == 0)
                        q.Enqueue(v);
                }
            }

            if (order.Count != taskIds.Count)
            {
                error = "Cycle detected in dependencies (not a DAG).";
                return null;
            }

            return order;
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
    }
}
