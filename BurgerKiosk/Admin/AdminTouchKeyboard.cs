using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace BurgerKiosk;

// Uses WPF's own text editor, so selection/caret editing also works in PasswordBox.
// Never copies credentials through the clipboard or an OS-wide keyboard API.
public static class AdminTouchKeyboard
{
    public static void Insert(Control target, string text)
    {
        if (!target.IsEnabled || string.IsNullOrEmpty(text)) return;
        target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
            new TextComposition(InputManager.Current, target, text))
        { RoutedEvent = TextCompositionManager.TextInputEvent });
    }

    public static void Backspace(Control target) => EditingCommands.Backspace.Execute(null, target);
    public static void Left(Control target) => EditingCommands.MoveLeftByCharacter.Execute(null, target);
    public static void Right(Control target) => EditingCommands.MoveRightByCharacter.Execute(null, target);
    public static void Clear(Control target)
    {
        if (target is PasswordBox password) password.Clear();
        else if (target is TextBox text) text.Clear();
    }
}
