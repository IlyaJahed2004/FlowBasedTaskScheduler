namespace FlowBasedTaskScheduler;

class Program
{
    static void Main()
    {
        // Example tasks with deadlines
        var tasks = new List<DataModel.Task> { new("T1", 2, 4, 2), new("T2", 1, 2, 3) };

        var nodes = new List<DataModel.Node> { new("N1", 5, 6, 2), new("N2", 3, 3, 2) };

        // Execution cost matrix (tasks x nodes)
        // int.MaxValue = not executable
        int[,] costMatrix =
        {
            { 4, 6 },
            { 3, 2 },
        };

        var allocator = new Allocation.TaskAllocator(tasks, nodes, costMatrix);
        string json = allocator.GetPhase1OutputJson();

        Console.WriteLine("=== Phase 1 JSON Output ===");
        Console.WriteLine(json);
    }
}
