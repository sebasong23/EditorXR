﻿#if UNITY_EDITOR
using System;

namespace UnityEditor.Experimental.EditorVR.Input
{
    interface IInputToEvents
    {
        bool active { get; }
        event Action activeChanged;
    }
}

#endif
