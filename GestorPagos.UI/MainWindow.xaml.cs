using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestorPagos.Core.Entities;
using GestorPagos.Data;

namespace GestorPagos.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow(ViewModels.MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Card_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    var viewModel = DataContext as ViewModels.MainViewModel;
                    viewModel?.ImportCommand.Execute(files[0]);
                }
            }
        }

        /// <summary>
        /// Doble clic en una fila para corregir el pago
        /// </summary>
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid?.SelectedItem is Pago pago)
            {
                var viewModel = DataContext as ViewModels.MainViewModel;
                viewModel?.CorregirPagoCommand?.Execute(pago);
            }
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column.Header.ToString() == "CC")
            {
                var textBox = e.EditingElement as TextBox;
                if (e.Row.Item is Pago pago && textBox != null)
                {
                    // Obtener el nuevo valor del TextBox
                    string nuevoCC = textBox.Text;
                    
                    // Actualizar el modelo inmediatamente para evitar problemas de binding
                    pago.CC = nuevoCC;

                    var viewModel = DataContext as ViewModels.MainViewModel;
                    // Llamar al método de procesamiento en el ViewModel
                    viewModel?.ProcesarCambioManualCC(pago);
                }
            }
        }
    }
}