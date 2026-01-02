using System.Reflection;

namespace Akagi.CharacterEditor.UndoRedo;

public class ChangePropertyAction : IUndoableAction
{
    private readonly PropertyInfo _propertyInfo;
    private readonly object _instance;
    private readonly object? _oldValue;
    private readonly object? _newValue;
    private readonly NodePropertyViewModel? _propertyViewModel;
    private readonly string _propertyName;

    public string Description => $"Change {_propertyName}";

    public ChangePropertyAction(PropertyInfo propertyInfo, object instance, object? oldValue, object? newValue, NodePropertyViewModel? propertyViewModel = null)
    {
        _propertyInfo = propertyInfo;
        _instance = instance;
        _oldValue = oldValue;
        _newValue = newValue;
        _propertyViewModel = propertyViewModel;
        _propertyName = propertyInfo.Name;
    }

    public void Undo()
    {
        _propertyInfo.SetValue(_instance, _oldValue);
        UpdateViewModelValue(_oldValue);
    }

    public void Redo()
    {
        _propertyInfo.SetValue(_instance, _newValue);
        UpdateViewModelValue(_newValue);
    }

    private void UpdateViewModelValue(object? value)
    {
        if (_propertyViewModel == null)
        {
            return;
        }

        Type propertyType = _propertyInfo.PropertyType;

        if (propertyType.IsEnum)
        {
            if (_propertyViewModel.IsFlagsEnum && _propertyViewModel.FlagsEnumItems != null && value != null)
            {
                // Update flags enum checkboxes
                int currentValue = Convert.ToInt32(value);
                
                foreach (FlagsEnumItemViewModel item in _propertyViewModel.FlagsEnumItems)
                {
                    int flagValue = Convert.ToInt32(item.EnumValue);
                    item.SetCheckedSilent((currentValue & flagValue) == flagValue);
                }
            }
            else
            {
                _propertyViewModel.SelectedEnumValue = value;
            }
        }
        else if (propertyType == typeof(bool))
        {
            _propertyViewModel.BoolValue = (bool)(value ?? false);
        }
        else if (propertyType == typeof(int))
        {
            _propertyViewModel.IntValue = (int)(value ?? 0);
        }
        else if (propertyType == typeof(double))
        {
            _propertyViewModel.DoubleValue = (double)(value ?? 0.0);
        }
        else if (propertyType == typeof(float))
        {
            _propertyViewModel.FloatValue = (float)(value ?? 0.0f);
        }
        else if (propertyType == typeof(string))
        {
            _propertyViewModel.StringValue = value?.ToString() ?? string.Empty;
        }

        // Update node title if this is the Name property
        if (_propertyName == "Name" && _propertyViewModel.ParentNode != null)
        {
            _propertyViewModel.ParentNode.UpdateTitleFromNameProperty();
        }
    }
}
