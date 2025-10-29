using NetArchTest.Rules;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NetSdrClientAppTests
{
    public class ArchitectureTests
    {
        [Test]
        public void App_Should_Not_Depend_On_EchoServer()
        {
            var result = Types.InAssembly(typeof(NetSdrClientApp.NetSdrClient).Assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp")
                .ShouldNot()
                .HaveDependencyOn("EchoServer")
                .GetResult();

            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void Messages_Should_Not_Depend_On_Networking()
        {
            // Arrange
            var result = Types.InAssembly(typeof(NetSdrClientApp.Messages.NetSdrMessageHelper).Assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Networking")
                .GetResult();

            // Assert
            Assert.That(result.IsSuccessful, Is.True);
        }

        [Test]
        public void Networking_Should_Not_Depend_On_Messages()
        {
            // Arrange
            var result = Types.InAssembly(typeof(NetSdrClientApp.Networking.ITcpClient).Assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Networking")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Messages")
                .GetResult();

            // Assert
            Assert.That(result.IsSuccessful, Is.True);
        }
        
        [Test]
        public void Messages_Namespace_Should_Exist()
        {
            var result = Types.InAssembly(typeof(NetSdrClientApp.Messages.NetSdrMessageHelper).Assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .GetTypes();

            Assert.That(result.Any(), Is.True, "У збірці відсутні типи в просторі NetSdrClientApp.Messages!");
        }

        [Test]
        public void Each_Namespace_Should_Not_Have_Circular_Dependencies()
        {
            // Тут ми перевіряємо, що між внутрішніми частинами NetSdrClientApp немає взаємних залежностей
            // Наприклад: Networking -> Messages і Messages -> Networking одночасно.

            var assembly = typeof(NetSdrClientApp.NetSdrClient).Assembly;

            var result1 = Types.InAssembly(assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Networking")
                .GetResult();

            var result2 = Types.InAssembly(assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Networking")
                .ShouldNot()
                .HaveDependencyOn("NetSdrClientApp.Messages")
                .GetResult();

            Assert.That(result1.IsSuccessful && result2.IsSuccessful, Is.True,
                "Виявлено циклічні залежності між Messages і Networking у NetSdrClientApp!");
        }

        [Test]
        public void Messages_Should_Only_Depend_On_Allowed_Libraries()
        {
            // Тест гарантує, що простір NetSdrClientApp.Messages не має залежностей 
            // від інших частин застосунку (наприклад, Networking або EchoServer),
            // але допускає системні бібліотеки і ті NuGet-пакети, що є в проєкті.
            
            var result = Types.InAssembly(typeof(NetSdrClientApp.Messages.NetSdrMessageHelper).Assembly)
                .That()
                .ResideInNamespace("NetSdrClientApp.Messages")
                .Should()
                .OnlyHaveDependenciesOn(
                    "System",
                    "System.Text",
                    "System.Collections",
                    "System.Collections.Generic",
                    "System.Linq",
                    "System.IO",
                    "System.Threading.Tasks",
                    "System.Reflection.PortableExecutable",
                    "NetSdrClientApp.Messages",
                    "Newtonsoft.Json",
                    "ICSharpCode.SharpZipLib"
                )
                .GetResult();

            Assert.That(result.IsSuccessful, Is.True,
                "NetSdrClientApp.Messages має заборонені або зайві залежності!");
        }
    }
}