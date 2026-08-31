namespace StickyNotes.Services;

/// <summary>Confirmações de ações destrutivas (exclusão). Abstrai o MessageBox
/// para permitir teste das ViewModels.</summary>
public interface IConfirmationService
{
    /// <summary>Pergunta se o usuário quer excluir a nota. Retorna true se confirmou.</summary>
    bool ConfirmDelete(string noteTitle);
}
