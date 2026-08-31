using System.Windows;
using StickyNotes.Views;

namespace StickyNotes.Services;

/// <summary>Confirmações de ações destrutivas no visual do app (diálogo customizado).</summary>
public class ConfirmationService : IConfirmationService
{
    public bool ConfirmDelete(string noteTitle)
    {
        var dialog = new ConfirmDialog
        {
            Message = $"A nota \"{noteTitle}\" será excluída permanentemente.",
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive),
        };
        dialog.ShowDialog();
        return dialog.Confirmed;
    }
}
