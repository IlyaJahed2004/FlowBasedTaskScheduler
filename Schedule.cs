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
        private readonly HashSet<int> timeSlotSet;
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
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            this.nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            this.assignment = assignment ?? throw new ArgumentNullException(nameof(assignment));
            this.timeSlots = (timeSlots ?? throw new ArgumentNullException(nameof(timeSlots)))
                .OrderBy(t => t)
                .ToList();
            this.timeSlotSet = new HashSet<int>(this.timeSlots);
            this.cpuPerTime = DeepCopy(
                cpuPerTime ?? throw new ArgumentNullException(nameof(cpuPerTime))
            );
            this.ramPerTime = ramPerTime is null ? null : DeepCopy(ramPerTime);
            this.duration = duration ?? new Dictionary<string, int>();
            this.dependencies = dependencies ?? new List<Dependency>();
            this.totalCostPhase1 = totalCostPhase1;
        }

        /// <summary>
        /// Main solver: dynamic list-scheduling using ready-set.
        /// Tries to place tasks as early as possible while respecting dependencies, deadlines and per-slot resources.
        /// </summary>
        public Phase2Result Solve()
        {
            // quick lookups
            var taskById = tasks.ToDictionary(t => t.Id, t => t);
            var nodeById = nodes.ToDictionary(n => n.Id, n => n);

            // validate assignments and basic inputs
            foreach (var kv in assignment)
            {
                if (!taskById.ContainsKey(kv.Key))
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason = $"Assignment refers to unknown task {kv.Key}",
                    };
                if (!nodeById.ContainsKey(kv.Value))
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason = $"Assignment refers to unknown node {kv.Value} for task {kv.Key}",
                    };
            }

            // Build predecessor and successor sets + indegree
            var preds = tasks.ToDictionary(t => t.Id, t => new HashSet<string>());
            var succs = tasks.ToDictionary(t => t.Id, t => new HashSet<string>());
            var indeg = tasks.ToDictionary(t => t.Id, t => 0);

            foreach (var d in dependencies)
            {
                if (!preds.ContainsKey(d.Before) || !preds.ContainsKey(d.After))
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason = $"Dependency refers to unknown task: {d.Before} -> {d.After}",
                    };

                if (!succs[d.Before].Contains(d.After))
                {
                    succs[d.Before].Add(d.After);
                    preds[d.After].Add(d.Before);
                    indeg[d.After] = preds[d.After].Count;
                }
            }

            // ready set: tasks with indeg = 0
            var ready = new SortedSet<string>(
                indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key)
            );

            // state
            var finishTime = new Dictionary<string, int>(); // taskId -> finish time
            var schedule = new Dictionary<string, (string node, int start_time)>();

            // Helper local funcs for resource checks (work on copies already)
            bool CanFit(string nodeId, int start, int dur, int cpuReq, int ramReq)
            {
                if (!cpuPerTime.ContainsKey(nodeId))
                    return false;
                for (int k = 0; k < dur; k++)
                {
                    int t = start + k;
                    if (!timeSlotSet.Contains(t))
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

            void Reserve(string nodeId, int start, int dur, int cpuReq, int ramReq)
            {
                for (int k = 0; k < dur; k++)
                {
                    int t = start + k;
                    cpuPerTime[nodeId][t] -= cpuReq;
                    if (ramPerTime != null)
                        ramPerTime[nodeId][t] -= ramReq;
                }
            }

            // main loop: dynamic ready-set scheduling
            // We'll pick among ready tasks the one with the smallest earliest feasible start,
            // tie-break by earliest deadline then larger cpu requirement (put heavy tasks earlier).
            var remaining = new HashSet<string>(taskById.Keys);

            while (remaining.Count > 0)
            {
                // populate ready if empty (shouldn't by construction, but safety)
                if (ready.Count == 0)
                {
                    // cycle or unsatisfiable dependency (shouldn't happen because we validated DAG earlier)
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason =
                            "No ready tasks available — possible cycle or missing predecessors.",
                    };
                }

                // For each ready task compute est (max of preds finish), latestStart and earliest feasible start (or INF)
                var candidateInfos =
                    new List<(string taskId, int est, int latestStart, int earliestFit)>();
                foreach (var tid in ready)
                {
                    var tsk = taskById[tid];
                    int d = duration.TryGetValue(tid, out var dd) ? dd : 1;
                    int est = 0;
                    foreach (var p in preds[tid])
                    {
                        if (!finishTime.TryGetValue(p, out var pf))
                        {
                            // predecessor not scheduled yet (should not happen for ready tasks)
                            est = int.MaxValue / 4;
                            break;
                        }
                        est = Math.Max(est, pf);
                    }

                    int latestStart = tsk.deadline - d;
                    if (latestStart < est)
                    {
                        // impossible for this task in any schedule (deadline too tight)
                        candidateInfos.Add((tid, est, latestStart, int.MaxValue / 4));
                        continue;
                    }

                    // find earliest start >= est s.t. CanFit
                    int earliestFit = int.MaxValue / 4;
                    for (int s = est; s <= latestStart; s++)
                    {
                        if (CanFit(assignment[tid], s, d, tsk.CpuRequired, tsk.RamRequired))
                        {
                            earliestFit = s;
                            break;
                        }
                    }

                    candidateInfos.Add((tid, est, latestStart, earliestFit));
                }

                // choose best candidate: first, any with finite earliestFit; pick smallest earliestFit;
                // tie-break by deadline, then by cpu requirement desc
                var feasible = candidateInfos
                    .Where(ci => ci.earliestFit < int.MaxValue / 4)
                    .ToList();
                (string taskId, int est, int latestStart, int earliestFit) chosenInfo;
                if (feasible.Count > 0)
                {
                    chosenInfo = feasible
                        .OrderBy(ci => ci.earliestFit)
                        .ThenBy(ci => taskById[ci.taskId].deadline)
                        .ThenByDescending(ci => taskById[ci.taskId].CpuRequired)
                        .First();
                }
                else
                {
                    // No ready task can fit now. Perhaps some ready tasks are blocked until other ready tasks are scheduled.
                    // Try a heuristic: pick ready task with largest slack (latestStart - est) negative means impossible
                    var impossible = candidateInfos
                        .Where(ci => ci.earliestFit >= int.MaxValue / 4)
                        .ToList();
                    // if every ready task impossible -> infeasible schedule
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason =
                            $"No ready task can be placed in any allowed time window. Check capacities/deadlines. Details: {string.Join(", ", impossible.Select(ii => $"{ii.taskId}(est={ii.est},latest={ii.latestStart})"))}",
                    };
                }

                // schedule chosen
                var chosenTaskId = chosenInfo.taskId;
                var chosenStart = chosenInfo.earliestFit;
                var chosenDur = duration.TryGetValue(chosenTaskId, out var dd2) ? dd2 : 1;
                var chosenNode = assignment[chosenTaskId];
                // final sanity checks
                if (
                    !CanFit(
                        chosenNode,
                        chosenStart,
                        chosenDur,
                        taskById[chosenTaskId].CpuRequired,
                        taskById[chosenTaskId].RamRequired
                    )
                )
                {
                    return new Phase2Result
                    {
                        Valid = false,
                        TotalCost = totalCostPhase1,
                        Reason =
                            $"Internal error: candidate chosen but cannot actually fit: {chosenTaskId} on {chosenNode} at {chosenStart}",
                    };
                }

                Reserve(
                    chosenNode,
                    chosenStart,
                    chosenDur,
                    taskById[chosenTaskId].CpuRequired,
                    taskById[chosenTaskId].RamRequired
                );
                schedule[chosenTaskId] = (chosenNode, chosenStart);
                finishTime[chosenTaskId] = chosenStart + chosenDur;

                // update sets
                remaining.Remove(chosenTaskId);
                ready.Remove(chosenTaskId);

                // decrease indeg for successors (i.e., remove edge chosenTask -> succ)
                foreach (var succ in succs[chosenTaskId])
                {
                    preds[succ].Remove(chosenTaskId);
                    indeg[succ] = preds[succ].Count;
                    if (indeg[succ] == 0)
                        ready.Add(succ);
                }
            }

            // all scheduled
            return new Phase2Result
            {
                Valid = true,
                TotalCost = totalCostPhase1,
                Schedule = schedule,
            };
        }

        private static Dictionary<string, Dictionary<int, int>> DeepCopy(
            Dictionary<string, Dictionary<int, int>> src
        )
        {
            var dst = new Dictionary<string, Dictionary<int, int>>();
            foreach (var kv in src)
            {
                dst[kv.Key] = kv.Value.ToDictionary(x => x.Key, x => x.Value);
            }
            return dst;
        }
    }
}
