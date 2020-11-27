using System;

namespace Entia
{
    static class Program
    {
        static void Main()
        {
            // Json.Benches.Run();
            Check.Checks.Run();
            Core.Checks.Run();
            Json.Checks.Run();
            Console.ReadLine();
            Tests.Run();
        }
    }
}
