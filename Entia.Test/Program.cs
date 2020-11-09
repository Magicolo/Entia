using System;

namespace Entia
{
    static class Program
    {
        static void Main()
        {
            Check.Checks.Run();
            Corez.Checks.Run();
            Json.Checks.Run();
            Console.ReadLine();
            Tests.Run();
        }
    }
}
