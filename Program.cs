using System;
using System.Collections.Generic;
using System.Linq;
using Allocation;
using DataModel;
using DynamicReallocation;
using LocalNodeScheduling;
using Scheduling;

namespace FlowBasedTaskScheduler
{
    class Program
    {
        static void Main()
        {
            // --- Phase 0: Define Tasks & Nodes ---
            var tasks = new List<DataModel.Task>
            {
                new DataModel.Task("T1", 2, 2, 2),
                new DataModel.Task("T2", 1, 2, 3),
                new DataModel.Task("T3", 2, 1, 4),
            };

            var nodes = new List<Node>
            {
                new Node("N1", 2, 2, 2),
                new Node("N2", 3, 2, 2),
                new Node("N3", 2, 2, 2),
            };

            // --- Phase 1: Allocation ---
            int[,] costMatrix =
            {
                { 3, 5, 4 },
                { 2, 3, 3 },
                { 4, 2, 3 },
            };

            var allocator = new TaskAllocator(tasks, nodes, costMatrix);
            var (flow, phase1Cost, allocation) = allocator.Solve();

            Console.WriteLine("=== Phase 1: Assignment ===");
            foreach (var (t, n) in allocation)
                Console.WriteLine($"  {t.Id} -> {n.Id}");
            Console.WriteLine(allocator.GetPhase1OutputJson());

            // --- Phase 2: Global Scheduling ---
            var timeSlots = new List<int> { 0, 1, 2, 3 };

            var cpuPerTime = new Dictionary<string, Dictionary<int, int>>
            {
                ["N1"] = new Dictionary<int, int>
                {
                    { 0, 2 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                },
                ["N2"] = new Dictionary<int, int>
                {
                    { 0, 3 },
                    { 1, 3 },
                    { 2, 3 },
                    { 3, 3 },
                },
                ["N3"] = new Dictionary<int, int>
                {
                    { 0, 2 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                },
            };

            var ramPerTime = new Dictionary<string, Dictionary<int, int>>
            {
                ["N1"] = new Dictionary<int, int>
                {
                    { 0, 2 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                },
                ["N2"] = new Dictionary<int, int>
                {
                    { 0, 2 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                },
                ["N3"] = new Dictionary<int, int>
                {
                    { 0, 2 },
                    { 1, 2 },
                    { 2, 2 },
                    { 3, 2 },
                },
            };

            var durations = new Dictionary<string, int>
            {
                { "T1", 1 },
                { "T2", 1 },
                { "T3", 2 },
            };

            var deps = new List<Dependency>
            {
                new Dependency("T1", "T3"),
                new Dependency("T2", "T3"),
            };

            var assignment = allocation.ToDictionary(p => p.Item1.Id, p => p.Item2.Id);

            var scheduler = new TimeAwareScheduler(
                tasks,
                nodes,
                assignment,
                timeSlots,
                cpuPerTime,
                ramPerTime,
                durations,
                deps,
                phase1Cost
            );

            var phase2Result = scheduler.Solve();

            Console.WriteLine("\n=== Phase 2: Global Scheduling ===");
            if (phase2Result.Valid)
            {
                foreach (var kv in phase2Result.Schedule.OrderBy(k => k.Key))
                    Console.WriteLine(
                        $"  {kv.Key} -> node={kv.Value.node}, start={kv.Value.start_time}"
                    );
            }
            else
            {
                Console.WriteLine($"Scheduling failed: {phase2Result.Reason}");
            }

            // --- Phase 3: Dynamic Reallocation ---
            var phase3Events = new List<DynamicEvent>
            {
                new DynamicEvent("node_failure", Node: "N2"),
                new DynamicEvent("new_task", Task: new DataModel.Task("T4", 2, 2, 4)),
            };

            var reallocator = new DynamicReallocator(
                tasks,
                nodes,
                durations,
                cpuPerTime,
                ramPerTime,
                phase1Cost,
                phase2Result.Schedule
            );

            var phase3Output = reallocator.ProcessEvents(phase3Events);

            Console.WriteLine("\n=== Phase 3: Dynamic Reallocation ===");
            Console.WriteLine(reallocator.ToJson(phase3Output));

            // --- Phase 4: Local Node Scheduling using DP ---
            Console.WriteLine("\n=== Phase 4: Local Node Scheduling ===");
            var nodesAfterPhase3 = new Dictionary<string, List<LocalTask>>();

            foreach (var kv in phase3Output.UpdatedSchedule)
            {
                var nodeId = kv.Value.node;
                if (!nodesAfterPhase3.ContainsKey(nodeId))
                    nodesAfterPhase3[nodeId] = new List<LocalTask>();

                var taskObj = tasks.FirstOrDefault(tt => tt.Id == kv.Key);
                if (taskObj == null)
                {
                    taskObj = new DataModel.Task(kv.Key, 2, 2, 4);
                    tasks.Add(taskObj);
                }

                int dur = durations.ContainsKey(kv.Key) ? durations[kv.Key] : 1;

                nodesAfterPhase3[nodeId]
                    .Add(
                        new LocalTask(
                            taskObj.Id,
                            taskObj.CpuRequired,
                            taskObj.RamRequired,
                            dur,
                            taskObj.deadline
                        )
                    );
            }

            foreach (var nodeTasks in nodesAfterPhase3)
            {
                var nodeId = nodeTasks.Key;

                Dictionary<int, Dictionary<string, int>> resourceForNode;

                if (cpuPerTime.TryGetValue(nodeId, out var cpuMapForNode))
                {
                    resourceForNode = cpuMapForNode.ToDictionary(
                        kv => kv.Key,
                        kv => new Dictionary<string, int> { { "cpu", kv.Value } }
                    );
                }
                else
                {
                    resourceForNode = timeSlots.ToDictionary(
                        t => t,
                        t => new Dictionary<string, int> { { "cpu", 0 } }
                    );
                }

                var input = new Phase4Input(nodeId, nodeTasks.Value, resourceForNode, timeSlots);

                var localScheduler = new LocalSchedulerDP(input);
                var phase4Result = localScheduler.Schedule();

                Console.WriteLine($"\nNode {nodeId} Schedule:");
                Console.WriteLine(localScheduler.ToJson(phase4Result));
            }

            Console.WriteLine("\n(End of demo)");
        }
    }
}
