using Allocation;
using Scheduling;

namespace FlowBasedTaskScheduler;

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

        var nodes = new List<DataModel.Node>
        {
            new DataModel.Node("N1", CpuCapacity: 5, RamCapacity: 6, Slots: 2),
            new DataModel.Node("N2", CpuCapacity: 6, RamCapacity: 5, Slots: 2),
        };

        // --- Phase 1: execution cost matrix ---
        int[,] costMatrix =
        {
            { 4, 2 }, // T1: N1=4, N2=2
            { 3, 4 }, // T2: N1=3, N2=4
            { 2, 3 }, // T3: N1=2, N2=3
        };

        var allocator = new TaskAllocator(tasks, nodes, costMatrix);
        var (flow, phase1Cost, allocation) = allocator.Solve();

        Console.WriteLine("=== Phase 1: Assignment ===");
        Console.WriteLine($"Assigned tasks (flow) = {flow}, total cost = {phase1Cost}");
        foreach (var (task, node) in allocation)
        {
            Console.WriteLine($"  {task.Id} -> {node.Id}");
        }

        Console.WriteLine("\nPhase1 JSON output:");
        Console.WriteLine(allocator.GetPhase1OutputJson());

        // --- Prepare Phase 2 inputs ---
        var timeSlots = new List<int> { 0, 1, 2, 3 };

        var cpuPerTime = new Dictionary<string, Dictionary<int, int>>
        {
            ["N1"] = new Dictionary<int, int>
            {
                [0] = 2,
                [1] = 2,
                [2] = 2,
                [3] = 2,
            },
            ["N2"] = new Dictionary<int, int>
            {
                [0] = 3,
                [1] = 3,
                [2] = 2,
                [3] = 2,
            },
        };

        var ramPerTime = new Dictionary<string, Dictionary<int, int>>
        {
            ["N1"] = new Dictionary<int, int>
            {
                [0] = 6,
                [1] = 6,
                [2] = 6,
                [3] = 6,
            },
            ["N2"] = new Dictionary<int, int>
            {
                [0] = 5,
                [1] = 5,
                [2] = 5,
                [3] = 5,
            },
        };

        var deps = new List<Dependency> { new Dependency("T1", "T3"), new Dependency("T2", "T3") };

        var durations = new Dictionary<string, int>
        {
            ["T1"] = 1,
            ["T2"] = 1,
            ["T3"] = 2,
        };

        var assignment = allocation.ToDictionary(p => p.Item1.Id, p => p.Item2.Id);

        // --- Phase 2: schedule ---
        var scheduler = new TimeAwareScheduler(
            tasks: tasks,
            nodes: nodes,
            assignment: assignment,
            timeSlots: timeSlots,
            cpuPerTime: cpuPerTime,
            ramPerTime: ramPerTime,
            duration: durations,
            dependencies: deps,
            totalCostPhase1: phase1Cost
        );

        var res = scheduler.Solve();

        Console.WriteLine("\n=== Phase 2: Scheduling ===");
        Console.WriteLine($"Valid schedule: {res.Valid}");
        Console.WriteLine($"Total cost (from Phase1): {res.TotalCost}");

        if (res.Valid)
        {
            Console.WriteLine("Schedule:");
            foreach (var kv in res.Schedule.OrderBy(k => k.Key))
            {
                Console.WriteLine(
                    $"  {kv.Key} -> node={kv.Value.node}, start={kv.Value.start_time}"
                );
            }
        }
        else
        {
            Console.WriteLine($"Scheduling failed. Reason: {res.Reason}");
        }

        Console.WriteLine("\n(End of demo)");
    }
}
