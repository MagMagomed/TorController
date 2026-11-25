using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using TorController.App;
using TorController.App.Presentation;

namespace TorController.Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class CreateTorServiceWindowTests
{
    [TestMethod]
    public void WindowBindsCreationFieldsAndSubmitCommand()
    {
        RunOnSta(() =>
        {
            var viewModel = new CreateTorServiceViewModel();
            var window = new CreateTorServiceWindow(viewModel);
            var executablePathComboBox = (ComboBox)window.FindName("TorExecutableComboBox")!;

            Assert.AreSame(viewModel, window.DataContext);
            Assert.IsInstanceOfType(window.FindName("ServiceNameTextBox"), typeof(TextBox));
            Assert.IsInstanceOfType(executablePathComboBox, typeof(ComboBox));
            Assert.IsTrue(executablePathComboBox.IsEditable);
            Assert.IsTrue(BindingOperations.IsDataBound(
                executablePathComboBox,
                ComboBox.ItemsSourceProperty));
            Assert.IsTrue(BindingOperations.IsDataBound(
                executablePathComboBox,
                ComboBox.TextProperty));
            Assert.IsInstanceOfType(window.FindName("DataDirectoryTextBox"), typeof(TextBox));
            Assert.IsInstanceOfType(window.FindName("CreateButton"), typeof(Button));
            Assert.IsTrue(BindingOperations.IsDataBound(
                (Button)window.FindName("CreateButton")!,
                Button.CommandProperty));
            window.Close();
        });
    }

    [TestMethod]
    public void SubmittedRequestClosesModalWindowAsAccepted()
    {
        RunOnSta(() =>
        {
            var viewModel = new CreateTorServiceViewModel
            {
                ServiceName = "TorNew",
                DisplayName = "New Tor Service",
                TorExecutablePath = @"C:\Tor\tor.exe",
                DataDirectory = @"C:\Tor\NewData"
            };
            var window = new CreateTorServiceWindow(viewModel);
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => viewModel.SubmitAsync().GetAwaiter().GetResult()));

            var result = window.ShowDialog();

            Assert.IsTrue(result);
            Assert.IsNotNull(viewModel.Request);
        });
    }

    [TestMethod]
    public void EditableExecutablePathUpdatesViewModelForManualInput()
    {
        RunOnSta(() =>
        {
            var viewModel = new CreateTorServiceViewModel();
            var window = new CreateTorServiceWindow(viewModel);
            var executablePathComboBox = (ComboBox)window.FindName("TorExecutableComboBox")!;
            window.Show();

            executablePathComboBox.Text = @"D:\CustomTor\tor.exe";
            executablePathComboBox.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();

            Assert.AreEqual(@"D:\CustomTor\tor.exe", viewModel.TorExecutablePath);
            window.Close();
        });
    }

    [TestMethod]
    public void CreationFieldsHaveFieldSpecificTooltipsWithExpectedValuesAndExamples()
    {
        RunOnSta(() =>
        {
            var window = new CreateTorServiceWindow(new CreateTorServiceViewModel());
            var tooltipControls = new (string Name, string ExpectedPurpose)[]
            {
                ("ServiceNameTextBox", "системное имя"),
                ("DisplayNameTextBox", "понятное название"),
                ("TorExecutableComboBox", "исполняемый файл Tor"),
                ("DataDirectoryTextBox", "папка данных"),
                ("StartingTypeComboBox", "когда Windows запускает службу"),
                ("SocksPortsTextBox", "SOCKS-порты"),
                ("UseBridgesCheckBox", "подключаться ли к Tor через мосты"),
                ("BridgesTextBox", "параметры мостов Tor"),
                ("ClientTransportPluginsTextBox", "транспортного плагина"),
                ("LogsTextBox", "журнал Tor"),
                ("ControlPortsTextBox", "порт управления Tor")
            };

            foreach (var (name, expectedPurpose) in tooltipControls)
            {
                var control = window.FindName(name) as FrameworkElement;
                Assert.IsNotNull(control, $"Expected form control '{name}'.");
                Assert.IsInstanceOfType(control.ToolTip, typeof(string), $"Expected tooltip for '{name}'.");
                var tooltip = (string)control.ToolTip;
                StringAssert.Contains(tooltip, "Что:");
                StringAssert.Contains(tooltip, "Зачем:");
                StringAssert.Contains(tooltip, "Ожидается:");
                StringAssert.Contains(tooltip, "Пример:");
                StringAssert.Contains(tooltip, expectedPurpose);
            }

            var bridgeTooltip = (string)((FrameworkElement)window.FindName("UseBridgesCheckBox")!).ToolTip;
            StringAssert.Contains(bridgeTooltip, "включено");
            StringAssert.Contains(bridgeTooltip, "выключено");
            window.Close();
        });
    }

    [TestMethod]
    public void ConstructorRejectsNullViewModel()
    {
        RunOnSta(() =>
            Assert.ThrowsException<ArgumentNullException>(() => new CreateTorServiceWindow(null!)));
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
