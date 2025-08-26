using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.Logistics.Transport;

public class TransportBox : Entity<int>
{
    private List<TransportBoxItem> _items = new();
    private List<TransportBoxStateLog> _stateLog = new();

    public IReadOnlyList<TransportBoxItem> Items => _items;

    public string? Code { get; private set; }
    public TransportBoxState State { get; private set; } = TransportBoxState.New;
    public TransportBoxState DefaultReceiveState { get; private set; } = TransportBoxState.Stocked;
    public string? Description { get; set; }
    public DateTime? LastStateChanged { get; set; }

    public string? Location { get; set; }

    // Audit fields
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    
    // EF Core managed concurrency stamp - required for backward compatibility with original system
    public string? ConcurrencyStamp { get; set; }
    
    // Extra properties for backward compatibility with original system
    public string? ExtraProperties { get; set; }

    public IReadOnlyList<TransportBoxStateLog> StateLog => _stateLog;

    public static Expression<Func<TransportBox, bool>> IsInTransportPredicate = b => b.State == TransportBoxState.InTransit || b.State == TransportBoxState.Received || b.State == TransportBoxState.Opened;
    public static Func<TransportBox, bool> IsInTransportFunc = IsInTransportPredicate.Compile();
    public bool IsInTransit => IsInTransportFunc(this);

    public static Expression<Func<TransportBox, bool>> IsInReservePredicate = b => b.State == TransportBoxState.Reserve;
    public static Func<TransportBox, bool> IsInReserveFunc = IsInReservePredicate.Compile();
    public bool IsInReserve => IsInReserveFunc(this);

    public TransportBoxState? NextState => TransitionNode.NextState?.NewState;
    public TransportBoxState? PreviousState => TransitionNode.PreviousState?.NewState;
    public TransportBoxStateNode TransitionNode => _transitions[State];

    public void Open(string boxCode, DateTime date, string userName)
    {
        if (State != TransportBoxState.New)
        {
            throw new ValidationException($"Box number can only be assigned to boxes in 'New' state. Current state: {State}");
        }

        if (string.IsNullOrWhiteSpace(boxCode))
        {
            throw new ValidationException("Box code cannot be null or empty");
        }

        // Validate box code format (B + 3 digits)
        if (!IsValidBoxCodeFormat(boxCode))
        {
            throw new ValidationException("Box code must follow format: B + 3 digits (e.g., B001, B123)");
        }
        ChangeState(TransportBoxState.Opened, date, userName, TransportBoxState.New);
        Code = boxCode;
    }

    private static bool IsValidBoxCodeFormat(string boxCode)
    {
        if (boxCode.Length != 4) return false;
        if (boxCode[0] != 'B') return false;
        return boxCode.Substring(1).All(char.IsDigit);
    }

    public void RevertToOpened(DateTime date, string userName)
    {
        // Revert transition from InTransit or Reserve back to Opened state
        if (State != TransportBoxState.InTransit && State != TransportBoxState.Reserve)
        {
            throw new ValidationException($"Cannot revert to Opened from {State} state. Only InTransit and Reserve states can be reverted.");
        }

        if (string.IsNullOrEmpty(Code))
        {
            throw new ValidationException("Cannot revert to Opened: Box code is required");
        }

        ChangeState(TransportBoxState.Opened, date, userName, TransportBoxState.InTransit, TransportBoxState.Reserve);
    }


    public TransportBoxItem AddItem(string productCode, string productName, double amount, DateTime date, string userName)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var newItem = new TransportBoxItem(productCode, productName, amount, date, userName);
        _items.Add(newItem);

        return newItem;
    }

    public TransportBoxItem? DeleteItem(int itemId)
    {
        CheckState(TransportBoxState.Opened, TransportBoxState.Opened);
        var toDelete = _items.SingleOrDefault(s => s.Id == itemId);
        if (toDelete != null)
        {
            _items.Remove(toDelete);
        }
        return toDelete;
    }


    public void Reset(DateTime date, string userName)
    {
        // According to specification: Reset is allowed only from Opened state  
        _items.Clear();
        Code = null;
        ChangeState(TransportBoxState.New, date, userName, TransportBoxState.Opened);
    }

    public void ToTransit(DateTime date, string userName)
    {
        if (!_items.Any())
        {
            throw new ValidationException("Cannot transition to InTransit state: Box must contain at least one item");
        }

        ChangeState(TransportBoxState.InTransit, date, userName, TransportBoxState.Opened, TransportBoxState.Error);
    }

    public void ConfirmTransit(string confirmedBoxNumber, DateTime date, string userName)
    {
        if (string.IsNullOrWhiteSpace(confirmedBoxNumber))
        {
            throw new ValidationException("Box number confirmation is required");
        }

        if (confirmedBoxNumber != Code)
        {
            throw new ValidationException($"Box number mismatch: entered '{confirmedBoxNumber}' but expected '{Code}'");
        }

        ToTransit(date, userName);
    }

    public void ToReserve(DateTime date, string userName, TransportBoxLocation location)
    {
        Location = location.ToString();
        ChangeState(TransportBoxState.Reserve, date, userName, TransportBoxState.Opened, TransportBoxState.Error);
    }

    public void Receive(DateTime date, string userName, TransportBoxState receiveState = TransportBoxState.Stocked)
    {
        DefaultReceiveState = receiveState;
        ChangeState(TransportBoxState.Received, date, userName, TransportBoxState.InTransit, TransportBoxState.Reserve);
    }


    public void ToSwap(DateTime date, string userName)
    {
        // InSwap state is legacy - according to specification it's not in current state machine
        throw new ValidationException("InSwap state is not supported in current state machine specification");
    }

    public void ToPick(DateTime date, string userName)
    {
        // According to specification: ToPick (to Stocked) is allowed only from Received state
        ChangeState(TransportBoxState.Stocked, date, userName, TransportBoxState.Received);
    }

    public void Close(DateTime date, string userName)
    {
        // According to specification: Close is allowed from New, Received, and Stocked states
        ChangeState(TransportBoxState.Closed, date, userName, TransportBoxState.New, TransportBoxState.Received, TransportBoxState.Stocked);
    }

    private void ChangeState(TransportBoxState newState, DateTime now, string userName, params TransportBoxState[] allowedStates)
    {
        ChangeState(newState, now, userName, null, allowedStates);
    }

    private void ChangeState(TransportBoxState newState, DateTime now, string userName, string? description, TransportBoxState[] allowedStates)
    {
        CheckState(newState, allowedStates);

        if (description != null)
            Description += Environment.NewLine + description;
        State = newState;
        LastStateChanged = now;
        _stateLog.Add(new TransportBoxStateLog(newState, now, userName, description));
    }

    private void CheckState(TransportBoxState newState, params TransportBoxState[] allowedStates)
    {
        if (allowedStates.Any() && !allowedStates.Contains(State))
        {
            throw new ValidationException($"Unable to change state from {State} to {newState} ({string.Join(", ", allowedStates)} state is required for this action)");
        }
    }

    public void Error(DateTime date, string userName, string exMessage)
    {
        ChangeState(TransportBoxState.Error, date, userName, exMessage, Array.Empty<TransportBoxState>());
    }


    private static readonly Dictionary<TransportBoxState, TransportBoxStateNode> _transitions = new();

    static TransportBox()
    {
        // New → Opened, Closed
        var newNode = new TransportBoxStateNode();
        newNode.AddTransition(new TransportBoxAction(TransportBoxState.Opened, (box, newBoxNumber, time, userName) => box.Open(newBoxNumber!, time, userName), condition: b => b.Code != null));
        newNode.AddTransition(new TransportBoxAction(TransportBoxState.Closed, (box, newBoxNumber,time, userName) => box.Close(time, userName)));
        _transitions.Add(TransportBoxState.New, newNode);

        // Opened → InTransit, Reserve, New (reset)
        var openedNode = new TransportBoxStateNode();
        openedNode.AddTransition(new TransportBoxAction(TransportBoxState.InTransit, (box, newBoxNumber,time, userName) => box.ToTransit(time, userName)));
        openedNode.AddTransition(new TransportBoxAction(TransportBoxState.Reserve, (box, newBoxNumber,time, userName) => box.ToReserve(time, userName, TransportBoxLocation.Kumbal)));
        openedNode.AddTransition(new TransportBoxAction(TransportBoxState.New, (box, newBoxNumber,time, userName) => box.Reset(time, userName)));
        _transitions.Add(TransportBoxState.Opened, openedNode);

        // InTransit → Received, Opened (revert)
        var inTransitNode = new TransportBoxStateNode();
        inTransitNode.AddTransition(new TransportBoxAction(TransportBoxState.Received, (box, newBoxNumber,time, userName) => box.Receive(time, userName)));
        inTransitNode.AddTransition(new TransportBoxAction(TransportBoxState.Opened, (box, newBoxNumber,time, userName) => box.RevertToOpened(time, userName)));
        _transitions.Add(TransportBoxState.InTransit, inTransitNode);

        // Reserve → Received, Opened (revert)
        var reserveNode = new TransportBoxStateNode();
        reserveNode.AddTransition(new TransportBoxAction(TransportBoxState.Received, (box, newBoxNumber,time, userName) => box.Receive(time, userName)));
        reserveNode.AddTransition(new TransportBoxAction(TransportBoxState.Opened, (box, newBoxNumber,time, userName) => box.RevertToOpened(time, userName)));
        _transitions.Add(TransportBoxState.Reserve, reserveNode);

        // Received → Stocked, Closed
        var receivedNode = new TransportBoxStateNode();
        receivedNode.AddTransition(new TransportBoxAction(TransportBoxState.Stocked, (box, newBoxNumber,time, userName) => box.ToPick(time, userName)));
        receivedNode.AddTransition(new TransportBoxAction(TransportBoxState.Closed, (box,newBoxNumber, time, userName) => box.Close(time, userName)));
        _transitions.Add(TransportBoxState.Received, receivedNode);

        // Stocked → Closed
        var stockedNode = new TransportBoxStateNode();
        stockedNode.AddTransition(new TransportBoxAction(TransportBoxState.Closed, (box, newBoxNumber,time, userName) => box.Close(time, userName)));
        _transitions.Add(TransportBoxState.Stocked, stockedNode);

        // InSwap state transitions (legacy support)
        var inSwapNode = new TransportBoxStateNode();
        inSwapNode.AddTransition(new TransportBoxAction(TransportBoxState.Stocked, (box, newBoxNumber,time, userName) => box.ToPick(time, userName)));
        _transitions.Add(TransportBoxState.InSwap, inSwapNode);

        // Closed → No outbound transitions according to specification
        _transitions.Add(TransportBoxState.Closed, new TransportBoxStateNode());

        // Error → No outbound transitions according to specification
        _transitions.Add(TransportBoxState.Error, new TransportBoxStateNode());
    }

}