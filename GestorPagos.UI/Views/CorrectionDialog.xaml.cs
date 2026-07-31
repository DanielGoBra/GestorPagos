using System.Windows;
using GestorPagos.Core.Entities;

namespace GestorPagos.UI.Views
{
    public partial class CorrectionDialog : Window
    {
        public CorrectionDialog(Pago pago)
        {
            InitializeComponent();
            DataContext = pago;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
