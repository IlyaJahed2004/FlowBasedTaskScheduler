using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Allocation;
using DataModel;
using Scheduling;

namespace FlowBasedTaskScheduler
{
    class Program
    {
        static void Main()
        {
            // --- Phase 0: Define Tasks & Nodes (نمونه مطابق داک) ---
            var tasks = new List<DataModel.Task>
            {
                // Id, CpuRequired, RamRequired, deadline
                // توجه: deadline ها طوری انتخاب شدند که schedule نمونه امکان‌پذیر باشد
                new DataModel.Task("T1", 2, 4, 1), // duration=1 -> latestStart = 0
                new DataModel.Task("T2", 1, 2, 2), // duration=1 -> latestStart = 1
                new DataModel.Task("T3", 3, 3, 4), // duration=2 -> latestStart = 2
            };

            var nodes = new List<Node>
            {
                // Id, CpuCapacity, RamCapacity, Slots
                new Node("N1", 5, 6, 2), // می‌خواهیم T1 و T3 هر دو روی N1 بیایند -> Slots >= 2
                new Node("N2", 3, 3, 2),
            };

            // Phase1 cost matrix: tasks x nodes
            // تنظیم شده تا تخصیص مورد انتظار حاصل شود: T1->N1 (4), T2->N2 (2), T3->N1 (3)
            int[,] costMatrix =
            {
                { 4, 6 }, // T1 cost on N1,N2
                { 3, 2 }, // T2 cost on N1,N2
                { 3, 5 }, // T3 cost on N1,N2
            };

            var allocator = new TaskAllocator(tasks, nodes, costMatrix);

            // RUN SOLVE ONCE (Phase 1)
            var (flow, phase1Cost, allocation) = allocator.Solve();

            Console.WriteLine("=== Phase 1: Assignment ===");
            foreach (var (t, n) in allocation)
                Console.WriteLine($"  {t.Id} -> {n.Id}");

            // build assignment dictionary from allocation
            var assignments = new Dictionary<string, string>();
            foreach (var (t, n) in allocation)
                assignments[t.Id] = n.Id;

            // Diagnostic (optional)
            int sumByMatrix = 0;
            for (int i = 0; i < allocation.Count; i++)
            {
                var t = allocation[i].Item1;
                var n = allocation[i].Item2;
                int ti = tasks.FindIndex(x => x.Id == t.Id);
                int ni = nodes.FindIndex(x => x.Id == n.Id);
                if (ti >= 0 && ni >= 0)
                    sumByMatrix += costMatrix[ti, ni];
            }
            Console.WriteLine(
                $"Diagnostic: summed cost from costMatrix over assignments = {sumByMatrix}"
            );
            Console.WriteLine($"MCMF returned cost (phase1Cost) = {phase1Cost}");
            Console.WriteLine();

            // -------------------------
            // Phase 2: Time-aware scheduling (مطابق ورودی-خروجی داک)
            // -------------------------

            // time slots available (0..3) — T3 duration=2 will occupy slots 2 and 3 if start=2
            var timeSlots = new List<int> { 0, 1, 2, 3 };

            // Build per-node per-time CPU and RAM availability maps.
            // اینجا هر تایم‌اسلات ظرفیت کامل نود را دارد — مطابق سناریوی نمونه
            var cpuPerTime = new Dictionary<string, Dictionary<int, int>>();
            var ramPerTime = new Dictionary<string, Dictionary<int, int>>();
            foreach (var n in nodes)
            {
                var cpuMap = new Dictionary<int, int>();
                var ramMap = new Dictionary<int, int>();
                foreach (var t in timeSlots)
                {
                    cpuMap[t] = n.CpuCapacity;
                    ramMap[t] = n.RamCapacity;
                }
                cpuPerTime[n.Id] = cpuMap;
                ramPerTime[n.Id] = ramMap;
            }

            // Duration per task (time units) مطابق مثال داک
            var duration = new Dictionary<string, int>
            {
                ["T1"] = 1,
                ["T2"] = 1,
                ["T3"] = 2,
            };

            // Dependencies مطابق مثال داک: T1 & T2 باید قبل از T3 تموم بشن
            var dependencies = new List<Dependency>
            {
                new Dependency("T1", "T3"),
                new Dependency("T2", "T3"),
            };

            // Create scheduler and solve Phase 2
            var scheduler = new TimeAwareScheduler(
                tasks,
                nodes,
                assignments,
                timeSlots,
                cpuPerTime,
                ramPerTime,
                duration,
                dependencies,
                phase1Cost
            );

            var phase2 = scheduler.Solve();

            // Build output exactly in the requested JSON shape:
            // { "schedule": { "T1": {"node":"N1","start_time":0}, ... }, "valid": true, "total_cost": 9 }
            var scheduleOut = new Dictionary<string, object?>();
            if (phase2.Valid && phase2.Schedule != null)
            {
                foreach (var kv in phase2.Schedule)
                {
                    // kv.Value is (string node, int start_time)
                    scheduleOut[kv.Key] = new
                    {
                        node = kv.Value.node,
                        start_time = kv.Value.start_time,
                    };
                }
            }

            var finalOut = new
            {
                schedule = scheduleOut,
                valid = phase2.Valid,
                total_cost = phase1Cost,
                reason = phase2.Valid ? null : phase2.Reason,
            };

            Console.WriteLine("=== Phase 2: Result (requested format) ===");
            Console.WriteLine(
                JsonSerializer.Serialize(
                    finalOut,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );

            // Also print a friendly human-readable schedule
            Console.WriteLine();
            Console.WriteLine("Human-readable schedule (if valid):");
            if (!phase2.Valid)
            {
                Console.WriteLine($"Invalid schedule: {phase2.Reason}");
            }
            else
            {
                foreach (var kv in phase2.Schedule.OrderBy(k => k.Value.start_time))
                    Console.WriteLine(
                        $"  {kv.Key}: node={kv.Value.node}, start_time={kv.Value.start_time}"
                    );
            }
        }
    }
}
