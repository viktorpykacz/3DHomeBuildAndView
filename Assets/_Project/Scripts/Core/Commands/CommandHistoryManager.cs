using System;
using System.Collections.Generic;
using HomeBuilder.Data;
using UnityEngine;

namespace HomeBuilder.Core.Commands
{
    public interface IProjectCommand
    {
        Guid CommandId { get; }
        string Description { get; }
        void Execute();
        void Undo();
        bool TryMerge(IProjectCommand nextCommand);
    }

    /// <summary>
    /// Central Undo/Redo stack manager (Ctrl+Z / Ctrl+Y) for all CAD/BIM operations.
    /// </summary>
    public sealed class CommandHistoryManager
    {
        private readonly Stack<IProjectCommand> _undoStack = new();
        private readonly Stack<IProjectCommand> _redoStack = new();
        private readonly int _maxHistorySize;

        public event Action OnHistoryChanged;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public CommandHistoryManager(int maxHistorySize = 200)
        {
            _maxHistorySize = Mathf.Max(10, maxHistorySize);
        }

        public void ExecuteCommand(IProjectCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            command.Execute();

            if (_undoStack.Count > 0 && _undoStack.Peek().TryMerge(command))
            {
                _redoStack.Clear();
                OnHistoryChanged?.Invoke();
                return;
            }

            _undoStack.Push(command);
            _redoStack.Clear();
            TrimHistoryIfNeeded();
            OnHistoryChanged?.Invoke();
        }

        public void Undo()
        {
            if (!CanUndo) return;
            IProjectCommand cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);
            OnHistoryChanged?.Invoke();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            IProjectCommand cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);
            OnHistoryChanged?.Invoke();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnHistoryChanged?.Invoke();
        }

        private void TrimHistoryIfNeeded()
        {
            if (_undoStack.Count <= _maxHistorySize) return;
            IProjectCommand[] temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = _maxHistorySize - 1; i >= 0; i--)
            {
                _undoStack.Push(temp[i]);
            }
        }
    }

    /// <summary>
    /// Groups multiple atomic commands into a single Undo/Redo transaction.
    /// </summary>
    public sealed class CompositeProjectCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description { get; }
        private readonly IReadOnlyList<IProjectCommand> _commands;

        public CompositeProjectCommand(string description, IReadOnlyList<IProjectCommand> commands)
        {
            Description = description;
            _commands = commands;
        }

        public void Execute()
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                _commands[i].Execute();
            }
        }

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class CreateWallCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Utwórz ścianę";

        private readonly FloorData _floor;
        private readonly WallData _wallData;
        private readonly Action<FloorData> _onFloorTopologyChanged;

        public CreateWallCommand(FloorData floor, WallData wallData, Action<FloorData> onFloorTopologyChanged)
        {
            _floor = floor;
            _wallData = wallData;
            _onFloorTopologyChanged = onFloorTopologyChanged;
        }

        public void Execute()
        {
            _floor.Walls.Add(_wallData);
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _floor.Walls.Remove(_wallData);
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class PlaceFurnitureCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Wstaw mebel";

        private readonly FloorData _floor;
        private readonly FurnitureData _furniture;
        private readonly Action<FurnitureData, bool> _onFurnitureChanged;

        public PlaceFurnitureCommand(
            FloorData floor,
            FurnitureData furniture,
            Action<FurnitureData, bool> onFurnitureChanged)
        {
            _floor = floor;
            _furniture = furniture;
            _onFurnitureChanged = onFurnitureChanged;
        }

        public void Execute()
        {
            _floor.Furnitures.Add(_furniture);
            _onFurnitureChanged?.Invoke(_furniture, true);
        }

        public void Undo()
        {
            _floor.Furnitures.Remove(_furniture);
            _onFurnitureChanged?.Invoke(_furniture, false);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class PlaceMEPItemCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Osadź punkt instalacyjny MEP";

        private readonly WallData _hostWall;
        private readonly MEPData _mepItem;
        private readonly Action<WallData, MEPData, bool> _onMEPChanged;

        public PlaceMEPItemCommand(
            WallData hostWall,
            MEPData mepItem,
            Action<WallData, MEPData, bool> onMEPChanged)
        {
            _hostWall = hostWall;
            _mepItem = mepItem;
            _onMEPChanged = onMEPChanged;
        }

        public void Execute()
        {
            _hostWall.MountedMEPItems.Add(_mepItem);
            _onMEPChanged?.Invoke(_hostWall, _mepItem, true);
        }

        public void Undo()
        {
            _hostWall.MountedMEPItems.Remove(_mepItem);
            _onMEPChanged?.Invoke(_hostWall, _mepItem, false);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class DeleteWallCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Usuń ścianę";

        private readonly FloorData _floor;
        private readonly WallData _wallData;
        private readonly Action<FloorData> _onFloorTopologyChanged;

        public DeleteWallCommand(FloorData floor, WallData wallData, Action<FloorData> onFloorTopologyChanged)
        {
            _floor = floor;
            _wallData = wallData;
            _onFloorTopologyChanged = onFloorTopologyChanged;
        }

        public void Execute()
        {
            _floor.Walls.Remove(_wallData);
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            if (!_floor.Walls.Contains(_wallData))
            {
                _floor.Walls.Add(_wallData);
            }
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class ModifyWallPropertiesCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Zmień parametry ściany";

        private readonly FloorData _floor;
        private readonly WallData _wall;
        private readonly float _oldThickness;
        private readonly float _oldHeight;
        private float _newThickness;
        private float _newHeight;
        private readonly Action<FloorData> _onFloorTopologyChanged;

        public ModifyWallPropertiesCommand(
            FloorData floor,
            WallData wall,
            float oldThickness,
            float oldHeight,
            float newThickness,
            float newHeight,
            Action<FloorData> onFloorTopologyChanged)
        {
            _floor = floor;
            _wall = wall;
            _oldThickness = oldThickness;
            _oldHeight = oldHeight;
            _newThickness = newThickness;
            _newHeight = newHeight;
            _onFloorTopologyChanged = onFloorTopologyChanged;
        }

        public void Execute()
        {
            _wall.Thickness = _newThickness;
            _wall.BaseHeight = _newHeight;
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _wall.Thickness = _oldThickness;
            _wall.BaseHeight = _oldHeight;
            _onFloorTopologyChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand)
        {
            if (nextCommand is ModifyWallPropertiesCommand other && other._wall.WallId == _wall.WallId)
            {
                _newThickness = other._newThickness;
                _newHeight = other._newHeight;
                return true;
            }
            return false;
        }
    }

    public sealed class CalibrateBlueprintCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Kalibruj skalę rzutu 2D";

        private readonly BlueprintReferenceData _blueprint;
        private readonly float _oldMetersPerPixel;
        private readonly float _newMetersPerPixel;
        private readonly Action _onCalibrationApplied;

        public CalibrateBlueprintCommand(
            BlueprintReferenceData blueprint,
            float oldMetersPerPixel,
            float newMetersPerPixel,
            Action onCalibrationApplied)
        {
            _blueprint = blueprint;
            _oldMetersPerPixel = oldMetersPerPixel;
            _newMetersPerPixel = newMetersPerPixel;
            _onCalibrationApplied = onCalibrationApplied;
        }

        public void Execute()
        {
            _blueprint.MetersPerPixel = _newMetersPerPixel;
            _onCalibrationApplied?.Invoke();
        }

        public void Undo()
        {
            _blueprint.MetersPerPixel = _oldMetersPerPixel;
            _onCalibrationApplied?.Invoke();
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class CreateOpeningCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Wstaw otwór (okno/drzwi)";

        private readonly FloorData _floor;
        private readonly WallData _wall;
        private readonly OpeningData _opening;
        private readonly Action<FloorData> _onFloorChanged;

        public CreateOpeningCommand(
            FloorData floor,
            WallData wall,
            OpeningData opening,
            Action<FloorData> onFloorChanged)
        {
            _floor = floor;
            _wall = wall;
            _opening = opening;
            _onFloorChanged = onFloorChanged;
        }

        public void Execute()
        {
            _wall.Openings.Add(_opening);
            _onFloorChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _wall.Openings.Remove(_opening);
            _onFloorChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class DeleteOpeningCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Usuń otwór (okno/drzwi)";

        private readonly FloorData _floor;
        private readonly WallData _wall;
        private readonly OpeningData _opening;
        private readonly Action<FloorData> _onFloorChanged;

        public DeleteOpeningCommand(
            FloorData floor,
            WallData wall,
            OpeningData opening,
            Action<FloorData> onFloorChanged)
        {
            _floor = floor;
            _wall = wall;
            _opening = opening;
            _onFloorChanged = onFloorChanged;
        }

        public void Execute()
        {
            _wall.Openings.Remove(_opening);
            _onFloorChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            if (!_wall.Openings.Contains(_opening))
            {
                _wall.Openings.Add(_opening);
            }
            _onFloorChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class ConfigureAtticRoofCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Konfiguruj dach i ściankę kolankową";

        private readonly FloorData _floor;
        private readonly bool _oldIsAttic;
        private readonly float _oldKneeHeight;
        private readonly List<RoofPlaneData> _oldRoofPlanes;
        private readonly bool _newIsAttic;
        private readonly float _newKneeHeight;
        private readonly List<RoofPlaneData> _newRoofPlanes;
        private readonly Action<FloorData> _onFloorChanged;

        public ConfigureAtticRoofCommand(
            FloorData floor,
            bool newIsAttic,
            float newKneeHeight,
            List<RoofPlaneData> newRoofPlanes,
            Action<FloorData> onFloorChanged)
        {
            _floor = floor;
            _oldIsAttic = floor.IsAttic;
            _oldKneeHeight = floor.KneeWallHeight;
            _oldRoofPlanes = new List<RoofPlaneData>(floor.RoofPlanes);

            _newIsAttic = newIsAttic;
            _newKneeHeight = newKneeHeight;
            _newRoofPlanes = new List<RoofPlaneData>(newRoofPlanes);
            _onFloorChanged = onFloorChanged;
        }

        public void Execute()
        {
            _floor.IsAttic = _newIsAttic;
            _floor.KneeWallHeight = _newKneeHeight;
            _floor.RoofPlanes.Clear();
            _floor.RoofPlanes.AddRange(_newRoofPlanes);
            foreach (WallData w in _floor.Walls) w.ClipByRoofPlanes = _newIsAttic;
            _onFloorChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _floor.IsAttic = _oldIsAttic;
            _floor.KneeWallHeight = _oldKneeHeight;
            _floor.RoofPlanes.Clear();
            _floor.RoofPlanes.AddRange(_oldRoofPlanes);
            foreach (WallData w in _floor.Walls) w.ClipByRoofPlanes = _oldIsAttic;
            _onFloorChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class DeleteFurnitureCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Usuń mebel / szafkę";

        private readonly FloorData _floor;
        private readonly FurnitureData _furniture;
        private readonly Action<FloorData> _onFloorChanged;

        public DeleteFurnitureCommand(
            FloorData floor,
            FurnitureData furniture,
            Action<FloorData> onFloorChanged)
        {
            _floor = floor;
            _furniture = furniture;
            _onFloorChanged = onFloorChanged;
        }

        public void Execute()
        {
            _floor.Furnitures.Remove(_furniture);
            _onFloorChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            if (!_floor.Furnitures.Contains(_furniture))
            {
                _floor.Furnitures.Add(_furniture);
            }
            _onFloorChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class DeleteMEPItemCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Usuń punkt instalacyjny MEP";

        private readonly FloorData _floor;
        private readonly WallData _hostWall;
        private readonly RoomData _hostRoom;
        private readonly MEPData _mepItem;
        private readonly List<MEPConnectionLink> _removedLinks = new();
        private readonly Action<FloorData> _onFloorMEPChanged;

        public DeleteMEPItemCommand(
            FloorData floor,
            WallData hostWall,
            RoomData hostRoom,
            MEPData mepItem,
            Action<FloorData> onFloorMEPChanged)
        {
            _floor = floor;
            _hostWall = hostWall;
            _hostRoom = hostRoom;
            _mepItem = mepItem;
            _onFloorMEPChanged = onFloorMEPChanged;
        }

        public void Execute()
        {
            _hostWall?.MountedMEPItems.Remove(_mepItem);
            _hostRoom?.CeilingMEPItems.Remove(_mepItem);

            _removedLinks.Clear();
            for (int i = _floor.MEPLinks.Count - 1; i >= 0; i--)
            {
                MEPConnectionLink link = _floor.MEPLinks[i];
                if (link.SwitchMEPId == _mepItem.MEPId || link.TargetLightMEPId == _mepItem.MEPId)
                {
                    _removedLinks.Add(link);
                    _floor.MEPLinks.RemoveAt(i);
                }
            }

            _onFloorMEPChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            if (_hostWall != null && !_hostWall.MountedMEPItems.Contains(_mepItem))
            {
                _hostWall.MountedMEPItems.Add(_mepItem);
            }
            if (_hostRoom != null && !_hostRoom.CeilingMEPItems.Contains(_mepItem))
            {
                _hostRoom.CeilingMEPItems.Add(_mepItem);
            }

            foreach (MEPConnectionLink link in _removedLinks)
            {
                if (!_floor.MEPLinks.Contains(link))
                {
                    _floor.MEPLinks.Add(link);
                }
            }

            _onFloorMEPChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class PlaceCeilingLightCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Wstaw punkt świetlny sufitowy";

        private readonly FloorData _floor;
        private readonly RoomData _room;
        private readonly MEPData _lightItem;
        private readonly Action<FloorData> _onFloorMEPChanged;

        public PlaceCeilingLightCommand(
            FloorData floor,
            RoomData room,
            MEPData lightItem,
            Action<FloorData> onFloorMEPChanged)
        {
            _floor = floor;
            _room = room;
            _lightItem = lightItem;
            _onFloorMEPChanged = onFloorMEPChanged;
        }

        public void Execute()
        {
            _room.CeilingMEPItems.Add(_lightItem);
            _onFloorMEPChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _room.CeilingMEPItems.Remove(_lightItem);
            _onFloorMEPChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }

    public sealed class CreateMEPLinkCommand : IProjectCommand
    {
        public Guid CommandId { get; } = Guid.NewGuid();
        public string Description => "Połącz włącznik ze źródłem światła";

        private readonly FloorData _floor;
        private readonly MEPConnectionLink _link;
        private readonly Action<FloorData> _onFloorMEPChanged;

        public CreateMEPLinkCommand(
            FloorData floor,
            MEPConnectionLink link,
            Action<FloorData> onFloorMEPChanged)
        {
            _floor = floor;
            _link = link;
            _onFloorMEPChanged = onFloorMEPChanged;
        }

        public void Execute()
        {
            _floor.MEPLinks.Add(_link);
            _onFloorMEPChanged?.Invoke(_floor);
        }

        public void Undo()
        {
            _floor.MEPLinks.Remove(_link);
            _onFloorMEPChanged?.Invoke(_floor);
        }

        public bool TryMerge(IProjectCommand nextCommand) => false;
    }
}
