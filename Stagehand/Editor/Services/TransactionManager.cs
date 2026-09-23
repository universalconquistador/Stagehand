using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Stagehand.Editor.Services;

/// <summary>
/// An action that can be done and undone.
/// </summary>
public interface ITransaction : IDisposable
{
    /// <summary>
    /// The user-facing title of this transaction.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Whether this transaction affects the data model the user is editing.
    /// </summary>
    /// <remarks>
    /// For example, changing a property value <em>does</em> affect the data model, but changing which object
    /// is selected does <em>not</em>.
    /// <br />
    /// The primary use is when handling <see cref="ITransactionManager.TransactionDone"/> and Undone to see if
    /// the file needs saving to disk.
    /// </remarks>
    bool AffectsDataModel { get; }

    /// <summary>
    /// Performs the action associated with this transaction.
    /// </summary>
    /// <remarks>
    /// Can only be called when the transaction is not already done,
    /// i.e. it has not yet been done or it has been undone.
    /// </remarks>
    void Do();

    /// <summary>
    /// Reverses the effects of this transaction.
    /// </summary>
    /// <remarks>
    /// Can only be called when the transaction has already been done.
    /// </remarks>
    void Undo();

    /// <summary>
    /// Attempts to combine the given previous transaction into this one.
    /// </summary>
    /// <param name="previousTransaction">The previous transaction to combine with.</param>
    /// <returns>True if this transaction has been combined and the previous transaction can be disposed.</returns>
    bool TryCoalesce(ITransaction previousTransaction);
}

public abstract class TransactionBase : ITransaction
{
    public string Title { get; protected set; }
    public bool AffectsDataModel { get; protected set; }
    protected bool IsDone { get; private set; }

    protected List<IDisposable>? DoneDisposables { get; private set; } = null;
    protected List<IDisposable>? UndoneDisposables { get; private set; } = null;

    public TransactionBase(string title, bool affectsDataModel)
    {
        Title = title;
        AffectsDataModel = affectsDataModel;
    }

    public void Do()
    {
        Debug.Assert(!IsDone, "Transaction is already done!");

        OnDo();

        IsDone = true;
    }

    protected abstract void OnDo();

    public void Undo()
    {
        Debug.Assert(IsDone, "Transaction is already not done!");

        OnUndo();

        IsDone = false;
    }

    protected abstract void OnUndo();

    public virtual bool TryCoalesce(ITransaction previousTransaction)
    {
        return false;
    }

    /// <summary>
    /// Adds the given <see cref="IDisposable"/> to the list of disposables to dispose
    /// when this transaction is disposed.
    /// </summary>
    /// <param name="disposable">The disposable to dispose.</param>
    /// <param name="disposeWhenDone">Whether to dispose the given disposable when this transaction is disposed while having been done.</param>
    /// <param name="disposeWhenUndone">Whether to dispose the given disposable when this transaction is disposed while having been not done.</param>
    public void AddDisposable(IDisposable disposable, bool disposeWhenDone, bool disposeWhenUndone)
    {
        if (disposeWhenDone)
        {
            DoneDisposables ??= new();
            DoneDisposables.Add(disposable);
        }

        if (disposeWhenUndone)
        {
            UndoneDisposables ??= new();
            UndoneDisposables.Add(disposable);
        }
    }

    public void Dispose()
    {
        OnDispose();

        var disposables = IsDone ? DoneDisposables : UndoneDisposables;

        if (disposables != null)
        {
            // Last to first in case later items are dependant on previous items.
            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                disposables[i].Dispose();
            }
        }
    }

    protected virtual void OnDispose()
    { }
}

public class DelegateTransaction : TransactionBase
{
    private readonly Action _doAction;
    private readonly Action _undoAction;

    public DelegateTransaction(string title, Action doAction, Action undoAction, bool affectsDataModel)
        : base(title, affectsDataModel)
    {
        _doAction = doAction;
        _undoAction = undoAction;
    }

    protected override void OnDo()
    {
        _doAction.Invoke();
    }

    protected override void OnUndo()
    {
        _undoAction.Invoke();
    }
}

/// <summary>
/// A transaction that coalesces with other set property transactions that set the same property on the same object.
/// </summary>
/// <typeparam name="TObject">The type of object whose property is being set.</typeparam>
/// <typeparam name="TValue">The type of property that is being set.</typeparam>
public class SetPropertyTransaction<TObject, TValue> : TransactionBase
    where TObject : class
{
    private readonly string _propertyName;
    private readonly TObject _object;
    private readonly TValue _newValue;
    private TValue _oldValue;
    private readonly Action<TValue, TValue> _internalSetter;

    public SetPropertyTransaction(string? objectName, string propertyName, TObject @object, TValue newValue, TValue oldValue, Action<TValue, TValue> internalSetter, bool affectsDataModel = true, Func<TValue, string>? valueToString = null)
        : base($"Set {(objectName != null ? $"{objectName}'s " : "" )}{propertyName} to {valueToString?.Invoke(newValue) ?? newValue?.ToString() ?? "<null>"}", affectsDataModel)
    {
        _propertyName = propertyName;
        _object = @object;
        _newValue = newValue;
        _oldValue = oldValue;
        _internalSetter = internalSetter;
    }

    protected override void OnDo()
    {
        _internalSetter.Invoke(_newValue, _oldValue);
    }

    protected override void OnUndo()
    {
        _internalSetter.Invoke(_oldValue, _newValue);
    }

    public override bool TryCoalesce(ITransaction previousTransaction)
    {
        Debug.Assert(IsDone);

        if (previousTransaction is SetPropertyTransaction<TObject, TValue> previousSetTransaction)
        {
            Debug.Assert(previousSetTransaction.IsDone);

            if (previousSetTransaction._object == _object
                && previousSetTransaction._propertyName == _propertyName)
            {
                _oldValue = previousSetTransaction._oldValue;
                AffectsDataModel |= previousSetTransaction.AffectsDataModel;
                return true;
            }
        }

        return base.TryCoalesce(previousTransaction);
    }
}

/// <summary>
/// Manages the doing, undoing, and redoing of transactions.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// The title of the transaction that can be undone, if any.
    /// </summary>
    string? UndoTransactionTitle { get; }

    /// <summary>
    /// The title of the transaction that can be redone, if any.
    /// </summary>
    string? RedoTransactionTitle { get; }

    /// <summary>
    /// Raised after a transaction has been done or redone.
    /// </summary>
    event Action<ITransaction> TransactionDone;

    /// <summary>
    /// Raised after a transaction has been undone.
    /// </summary>
    event Action<ITransaction> TransactionUndone;

    /// <summary>
    /// Executes a transaction, adding it to the undo stack so that it can be undone and redone.
    /// </summary>
    /// <param name="transaction">The transaction to do.</param>
    void DoTransaction(ITransaction transaction);

    /// <summary>
    /// Enqueues the given action to be invoked after the current <see cref="DoTransaction(ITransaction)"/> is complete, or
    /// invokes it immediately if not in a <see cref="DoTransaction(ITransaction)"/>.
    /// </summary>
    /// <param name="completionAction">The action to invoke after the current transaction is recorded.</param>
    void QueueCompletionAction(Action completionAction);

    /// <summary>
    /// Begins the creation of a transaction group such that the transactions done until the corresponding
    /// <see cref="PopTransactionGroup"/> will all be done and undone together under the given title.
    /// </summary>
    /// <param name="title">The title of the transaction group.</param>
    void PushTransactionGroup(string title);

    /// <summary>
    /// Begins the creation of a transaction group such that transactions done until a call to
    /// <see cref="PopAutoReleaseGroups"/> will all be done and undone together under the given title.
    /// </summary>
    /// <param name="title">The title of the transaction group.</param>
    void PushAutoReleaseTransactionGroup(string title);

    /// <summary>
    /// Ends the creation of a transaction group that was begun with <see cref="PushTransactionGroup(string)"/>.
    /// </summary>
    /// <param name="adoptLastTitle">Whether to use the title of the most recently added transaction for this group.</param>
    void PopTransactionGroup(bool adoptLastTitle = false);

    /// <summary>
    /// Undoes the most recently done or redone transaction, if any.
    /// </summary>
    void Undo();

    /// <summary>
    /// Redoes the most recently undone transaction, if any.
    /// </summary>
    void Redo();

    /// <summary>
    /// Clears the undo and redo history, disposing the transactions and their disposables.
    /// </summary>
    void ClearHistory();

    /// <summary>
    /// Pops the groups from the top of the group stack that were created with <see cref="PushAutoReleaseTransactionGroup(string)"/>.
    /// </summary>
    void PopAutoReleaseGroups();
}

// TODO: Threading protections
internal class TransactionManager : ITransactionManager, IDisposable
{
    private class GroupTransaction : TransactionBase
    {
        private readonly List<ITransaction> _transactions = new();

        public GroupTransaction? OuterGroup { get; }
        public bool HasTransactions => _transactions.Count > 0;
        public bool AutoPop { get; }

        public GroupTransaction(string title, GroupTransaction? outerGroup, bool autoPop)
            : base(title, affectsDataModel: false)
        {
            OuterGroup = outerGroup;
            AutoPop = autoPop;

            // Group transactions aren't explicitly 'done' when they are first created, so we need
            // to mark this as starting out in the done state.
            Do();
        }

        public void AddTransaction(ITransaction transaction)
        {
            if (_transactions.Count > 0 && transaction.TryCoalesce(_transactions[_transactions.Count - 1]))
            {
                _transactions[_transactions.Count - 1].Dispose();
                _transactions[_transactions.Count - 1] = transaction;
            }
            else
            {
                _transactions.Add(transaction);
            }

            AffectsDataModel |= transaction.AffectsDataModel;
        }

        public void AdoptLastTitle()
        {
            if (_transactions.Count > 0)
            {
                Title = _transactions[_transactions.Count - 1].Title;
            }
        }

        protected override void OnDo()
        {
            foreach (var transaction in _transactions)
            {
                transaction.Do();
            }
        }

        protected override void OnUndo()
        {
            for (int i = _transactions.Count - 1; i >= 0; i--)
            {
                _transactions[i].Undo();
            }
        }

        protected override void OnDispose()
        {
            // Rather than adding every transaction to the done and undone dispose lists, let's
            // not allocate those lists and instead manually dispose the transactions here.
            // Last to first in case later items are dependant on previous items.
            for (int i = _transactions.Count - 1; i >= 0; i--)
            {
                _transactions[i].Dispose();
            }

            base.OnDispose();
        }
    }

    private readonly Stack<ITransaction> _undoStack = new();
    private readonly Stack<ITransaction> _redoStack = new();
    private readonly List<Action> _completionActions = new();

    private GroupTransaction? _currentGroupTransaction = null;
    private bool _isDoingTransaction = false;
    private bool _isUndoingTransaction = false;
    private bool _isRedoingTransaction = false;

    public string? UndoTransactionTitle => _undoStack.Count > 0 ? _undoStack.Peek().Title : null;
    public string? RedoTransactionTitle => _redoStack.Count > 0 ? _redoStack.Peek().Title : null;

    public event Action<ITransaction>? TransactionDone;
    public event Action<ITransaction>? TransactionUndone;

    private ITransaction? _currentlyDoingTransaction;
    private Lock _transactionDoLock = new();

    public void DoTransaction(ITransaction transaction)
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransaction("Cannot do transaction while doing or undoing a transaction or transaction group!");

            _isDoingTransaction = true;
            _currentlyDoingTransaction = transaction;
            try
            {
                transaction.Do();

                if (_currentGroupTransaction != null)
                {
                    _currentGroupTransaction.AddTransaction(transaction);
                }
                else
                {
                    ClearRedo();
                    _undoStack.Push(transaction);
                }
            }
            finally
            {
                _currentlyDoingTransaction = null;
                _isDoingTransaction = false;
            }

            TransactionDone?.Invoke(transaction);

            foreach (var completionAction in _completionActions)
            {
                completionAction.Invoke();
            }
            _completionActions.Clear();
        }
    }

    public void QueueCompletionAction(Action completionAction)
    {
        using (_transactionDoLock.EnterScope())
        {
            if (_isDoingTransaction)
            {
                // Queue up this action for when the Do is complete
                _completionActions.Add(completionAction);
            }
            else if (!_isUndoingTransaction && !_isRedoingTransaction)
            {
                // No transactions are being done, undone, or redone, so just run the action immediately
                completionAction.Invoke();
            }
        }
    }

    public void PushTransactionGroup(string title)
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransaction($"Cannot enter or exit a transaction group '{title}' while doing or undoing a transaction!");

            var newGroupTransaction = new GroupTransaction(title, _currentGroupTransaction, autoPop: false);
            _currentGroupTransaction = newGroupTransaction;
        }
    }

    public void PushAutoReleaseTransactionGroup(string title)
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransaction($"Cannot enter or exit a transaction group '{title}' while doing or undoing a transaction!");

            if (_currentGroupTransaction == null || !_currentGroupTransaction.AutoPop)
            {
                var newGroupTransaction = new GroupTransaction(title, _currentGroupTransaction, autoPop: true);
                _currentGroupTransaction = newGroupTransaction;
            }
        }
    }

    private void InternalPopGroup(bool adoptLastTitle)
    {
        var group = _currentGroupTransaction;

        if (group == null)
        {
            throw new InvalidOperationException("Tried to pop a group but there was no current group!");
        }

        _currentGroupTransaction = group.OuterGroup;

        if (group.HasTransactions)
        {
            if (adoptLastTitle)
            {
                group.AdoptLastTitle();
            }

            if (group.OuterGroup == null)
            {
                ClearRedo();
                _undoStack.Push(group);

                TransactionDone?.Invoke(group);
            }
            else
            {
                group.OuterGroup.AddTransaction(group);
            }
        }
    }

    public void PopTransactionGroup(bool adoptLastTitle = false)
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransaction($"Cannot enter or exit a transaction group '{_currentGroupTransaction?.Title}' while doing or undoing a transaction!");

            // Silently pop any auto pop groups to get to the next manual-pop group
            while (_currentGroupTransaction != null && _currentGroupTransaction.AutoPop)
            {
                InternalPopGroup(adoptLastTitle);
            }

            InternalPopGroup(adoptLastTitle);
        }    
    }

    public void Undo()
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransactionOrGroup("Cannot undo while doing or undoing a transaction or transaction group!");

            if (_undoStack.TryPop(out var transaction))
            {
                _isUndoingTransaction = true;
                try
                {
                    transaction.Undo();
                    _redoStack.Push(transaction);

                    TransactionUndone?.Invoke(transaction);
                }
                finally
                {
                    _isUndoingTransaction = false;
                }
            }
        }
    }

    public void Redo()
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransactionOrGroup("Cannot redo while doing or undoing a transaction or transaction group!");

            if (_redoStack.TryPop(out var transaction))
            {
                _isRedoingTransaction = true;
                try
                {
                    transaction.Do();
                    _undoStack.Push(transaction);

                    TransactionDone?.Invoke(transaction);
                }
                finally
                {
                    _isRedoingTransaction = false;
                }
            }
        }
    }

    public void ClearHistory()
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransactionOrGroup("Cannot clear history while doing or undoing a transaction or transaction group!");

            while (_undoStack.Count > 0)
            {
                _undoStack.Pop().Dispose();
            }

            ClearRedo();
        }
    }

    public void PopAutoReleaseGroups()
    {
        using (_transactionDoLock.EnterScope())
        {
            ThrowIfInTransaction($"Cannot enter or exit a transaction group '{_currentGroupTransaction?.Title}' while doing or undoing a transaction!");

            while (_currentGroupTransaction != null && _currentGroupTransaction.AutoPop)
            {
                InternalPopGroup(adoptLastTitle: true);
            }
        }
    }

    private void ClearRedo()
    {
        while (_redoStack.Count > 0)
        {
            _redoStack.Pop().Dispose();
        }
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if a transaction is currently being recorded,
    /// undone, or redone.
    /// </summary>
    private void ThrowIfInTransaction(string message)
    {
        if (_isDoingTransaction || _isUndoingTransaction || _isRedoingTransaction)
        {
            if (_currentlyDoingTransaction != null)
            {
                message += $" ({_currentlyDoingTransaction.Title})";
            }
            throw new InvalidOperationException(message);
        }
    }

    private void ThrowIfInTransactionOrGroup(string message)
    {
        ThrowIfInTransaction(message);

        if (_currentGroupTransaction != null)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose()
    {
        // Dispose any and all transactions
        ClearHistory();
    }
}

/// <summary>
/// A simple helper for pushing and popping a transaction group with a <c>using</c> statement.
/// </summary>
public struct TransactionScope : IDisposable
{
    private readonly ITransactionManager _transactionManager;

    internal TransactionScope(string title, ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
        _transactionManager.PushTransactionGroup(title);
    }

    public void Dispose()
    {
        _transactionManager.PopTransactionGroup();
    }
}

public static class TransactionManagerExtensions
{
    public static TransactionScope BeginTransactionGroup(this ITransactionManager transactionManager, string title)
    {
        return new TransactionScope(title, transactionManager);
    }
}
