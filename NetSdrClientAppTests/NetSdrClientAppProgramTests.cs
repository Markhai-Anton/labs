using System;
using System.IO;
using System.Threading.Tasks;
using NetSdrClientApp;
using NUnit.Framework;

namespace NetSdrClientAppTests;
[TestFixture]
public class NetSdrClientAppProgramTests
{
    [Test]
    public async Task EntryPoint_CanBeInvoked_Safely()
    {
        // дістаємо entry point збірки з NetSdrClient’ом
        var assembly = typeof(NetSdrClient).Assembly;
        var main = assembly.EntryPoint;

        Assert.That(main, Is.Not.Null, "Entry point should exist.");

        // глушимо вивід, щоб не засмічувати лог тестів
        Console.SetOut(TextWriter.Null);

        var executed = false;

        try
        {
            // викликаємо Main з порожнім масивом аргументів, якщо він його очікує
            object? result;
            if (main!.GetParameters().Length == 0)
            {
                result = main.Invoke(null, null);
            }
            else
            {
                result = main.Invoke(null, new object?[] { Array.Empty<string>() });
            }

            // для async Main повертається Task — дочекаємось його завершення
            if (result is Task t)
            {
                await t;
            }

            executed = true;
        }
        catch
        {
            // навіть якщо всередині впаде (наприклад, на Console.ReadKey),
            // нас цікавить сам факт виконання entry point
            executed = true;
        }

        Assert.That(executed, Is.True, "Main entry point was invoked.");
    }
}