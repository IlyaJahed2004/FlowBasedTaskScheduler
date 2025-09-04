using System;
using System.Collections.Generic;
using System.Linq;
using Allocation;
using DataModel;
using DynamicReallocation;
using Scheduling;

namespace FlowBasedTaskScheduler
{
    class Program
    {
        static void Main()
        {
            // --- Phase 0: define tasks and nodes ---
            var tasks = new List<DataModel.Task>
            {
                new DataModel.Task("T1", CpuRequired: 2, RamRequired: 4, deadline: 3),
                new DataModel.Task("T2", CpuRequired: 1, RamRequired: 2, deadline: 3),
                new DataModel.Task("T3", CpuRequired: 3, RamRequired: 3, deadline: 4),
            };

            var nodes = new List<Node>
            {
                new Node("N1", CpuCapacity: 5, RamCapacity: 6, Slots: 2),
                new Node("N2", CpuCapacity: 6, RamCapacity: 5, Slots: 2),
                new Node("N3", CpuCapacity: 4, RamCapacity: 4, Slots: 2),
            };

            // --- Phase 1: execution cost matrix ---
            int[,] costMatrix =
            {
                { 4, 2, 3 }, // T1: N1, N2, N3
                { 3, 4, 2 }, // T2
                { 2, 3, 4 }, // T3
            };

            var allocator = new TaskAllocator(tasks, nodes, costMatrix);
            var (flow, phase1Cost, allocation) = allocator.Solve();

            Console.WriteLine("=== Phase 1: Assignment ===");
            foreach (var (task, node) in allocation)
                Console.WriteLine($"  {task.Id} -> {node.Id}");
            Console.WriteLine(allocator.GetPhase1OutputJson());

            // --- Phase 2: schedule ---
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
            var ramPerTime = new Dictionary<string, Dictionary<int, int>>
            {
                ["N1"] = new Dictionary<int, int>
                {
                    { 0, 6 },
                    { 1, 6 },
                    { 2, 6 },
                    { 3, 6 },
                },
                ["N2"] = new Dictionary<int, int>
                {
                    { 0, 5 },
                    { 1, 5 },
                    { 2, 5 },
                    { 3, 5 },
                },
                ["N3"] = new Dictionary<int, int>
                {
                    { 0, 4 },
                    { 1, 4 },
                    { 2, 4 },
                    { 3, 4 },
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

            Console.WriteLine("\n=== Phase 2: Scheduling ===");
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
            Console.WriteLine("\n=== Phase 3: Dynamic Reallocation ===");

            // Sample dynamic events
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
            Console.WriteLine(reallocator.ToJson(phase3Output));

            Console.WriteLine("\n(End of demo)");
        }
    }
}
