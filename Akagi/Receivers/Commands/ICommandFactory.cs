﻿namespace Akagi.Receivers.Commands;

internal interface ICommandFactory
{
    public T Create<T>() where T : Command;

    public Command Create(string commandType);
}
