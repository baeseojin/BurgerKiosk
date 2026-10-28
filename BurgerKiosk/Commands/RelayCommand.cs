using System;
using System.Windows.Input;

namespace BurgerKiosk.Commands
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> execute;

        public RelayCommand(Action<object?> execute)
        {
            this.execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            execute(parameter);
        }
    }
}
