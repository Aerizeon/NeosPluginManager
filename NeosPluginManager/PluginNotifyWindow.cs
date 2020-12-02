using BaseX;
using FrooxEngine;
using FrooxEngine.UIX;
using System;
using System.Collections.Generic;
using System.Text;

namespace NeosPluginManager
{
    [GloballyRegistered]
    class PluginNotifyWindow : Component
    {
#pragma warning disable 0649
        protected readonly SyncRef<UserInterfacePositioner> _positioner;

        protected readonly SyncRef<Button> _continueButton;
        protected readonly SyncRef<Button> _cancelButton;
        protected readonly SyncRef<Text> _pluginText;
#pragma warning restore 0649

        private Action _successCallback = null;
        private Action _failureCallback = null;

        protected override void OnAttach()
        {
            if (!CheckUserspace())
                return;

            _positioner.Target = Slot.AttachComponent<UserInterfacePositioner>();
            Slot offsetSlot = Slot.AddSlot("Offset");
            Slot rotationSlot = offsetSlot.AddSlot("Rotation");
            Slot windowSlot = rotationSlot.AddSlot("Window");
            windowSlot.LocalScale = new float3(0.001f, 0.001f, 0.001f);
            windowSlot.LocalRotation = floatQ.AxisAngle(float3.Up, 180.0f);
            SmoothTransform windowSmoothTransform = windowSlot.AttachComponent<SmoothTransform>();
            windowSmoothTransform.Position.Target = null;
            windowSmoothTransform.Scale.Target = null;
            windowSmoothTransform.SmoothSpeed.Value = 10.0f;
            windowSmoothTransform.InterpolationSpace.UseLocalSpaceOf(Slot);

            offsetSlot.LocalPosition = (float3.Down * 0.25f) + (float3.Forward * 0.7f);
            offsetSlot.AttachComponent<DestroyBlock>();
            offsetSlot.AttachComponent<DuplicateBlock>();

            LookAt rotationLookAt = rotationSlot.AttachComponent<LookAt>();
            rotationLookAt.Target.Target = Slot;
            rotationLookAt.MaxSwing.Value = 0;
            rotationLookAt.MaxTwist.Value = 360f;
            rotationLookAt.SwingReference.Value = float3.Up;
            rotationLookAt.UpdateOrder = 1000000;
            windowSmoothTransform.UpdateOrder = rotationLookAt.UpdateOrder + 10;

            Canvas windowCanvas = windowSlot.AttachComponent<Canvas>();
            windowCanvas.Size.Value = new float2(512f, 512f);
            windowCanvas.StartingOffset.Value = 0;
            windowCanvas.Collider.Target.SetNoCollision();
            UIBuilder windowUI = new UIBuilder(windowCanvas);

            windowUI.Image(color.Black);

            windowUI.VerticalLayout(10f, 5f, Alignment.MiddleCenter);
            windowUI.PushStyle();
            windowUI.Style.TextColor = color.LightGray;
            _pluginText.Target = windowUI.Text("Plugin information will go here");
            windowUI.PopStyle();
            windowUI.HorizontalLayout(10f, 5f, Alignment.MiddleCenter);
            _continueButton.Target = windowUI.Button("OK", new ButtonEventHandler(Continue_Pressed));
            _cancelButton.Target = windowUI.Button("Cancel", new ButtonEventHandler(Cancel_Pressed));
            Slot.ActiveSelf = false;
        }

        private void Continue_Pressed(IButton button, ButtonEventData eventData)
        {
            _successCallback?.Invoke();
            Slot.ActiveSelf = false;
        }
        private void Cancel_Pressed(IButton button, ButtonEventData eventData)
        {
            _failureCallback?.Invoke();
            Slot.ActiveSelf = false;
        }

        public void ShowWindow(List<string> plugins, Action success, Action failure)
        {
            _successCallback = success;
            _failureCallback = failure;
            string pluginsString = string.Join(",\r\n", plugins);
            _pluginText.Target.Content.Value = $"The world you're trying to join requires the use of the following plugins:\r\n\r\n"
                + $"<color=red><noparse={pluginsString.Length}>" + pluginsString + "</color>\r\n\r\nIf this is acceptable, press OK";
            Slot.ActiveSelf = true;
        }
        protected override void OnStart() => CheckUserspace();
        private bool CheckUserspace()
        {
            if (this.World == Userspace.UserspaceWorld)
                return true;
            this.Debug.Warning("UserspaceDash must be added only to the Userspace!");
            this.Destroy();
            return false;
        }
    }
}
