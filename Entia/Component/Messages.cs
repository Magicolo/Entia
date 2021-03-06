﻿using Entia.Modules.Component;

namespace Entia.Messages
{
    public interface IComponentMessage : IMessage { }

    /// <summary>
    /// Message emitted after a component has been added to an entity.
    /// </summary>
    /// <seealso cref="IMessage" />
    public struct OnAdd : IComponentMessage
    {
        /// <summary>
        /// The entity that gained a component.
        /// </summary>
        public Entity Entity;
        /// <summary>
        /// The component type that was added.
        /// </summary>
        public Metadata Component;
    }

    /// <summary>
    /// Message emitted after a component of type <typeparamref name="T"/> has been added to an entity.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <seealso cref="IMessage" />
    public struct OnAdd<T> : IComponentMessage where T : struct, IComponent
    {
        /// <summary>
        /// The entity that gained a component of type <typeparamref name="T"/>.
        /// </summary>
        public Entity Entity;
    }

    /// <summary>
    /// Message emitted before a component will be removed from an entity.
    /// </summary>
    /// <seealso cref="IMessage" />
    public struct OnRemove : IComponentMessage
    {
        /// <summary>
        /// The entity that lost a component.
        /// </summary>
        public Entity Entity;
        /// <summary>
        /// The component type that was removed.
        /// </summary>
        public Metadata Component;
    }

    /// <summary>
    /// Message emitted before a component of type <typeparamref name="T"/> will be removed from an entity.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <seealso cref="IMessage" />
    public struct OnRemove<T> : IComponentMessage where T : struct, IComponent
    {
        /// <summary>
        /// The entity that lost a component of type <typeparamref name="T"/>.
        /// </summary>
        public Entity Entity;
    }

    /// <summary>
    /// Message emitted after a component has been enabled.
    /// </summary>
    /// <seealso cref="IMessage" />
    public struct OnEnable : IComponentMessage
    {
        /// <summary>
        /// The entity that had a component enabled.
        /// </summary>
        public Entity Entity;
        /// <summary>
        /// The component type that was enabled.
        /// </summary>
        public Metadata Component;
    }

    /// <summary>
    /// Message emitted after a component of type <typeparamref name="T"/> has been enabled.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <seealso cref="IMessage" />
    public struct OnEnable<T> : IComponentMessage where T : struct, IComponent
    {
        /// <summary>
        /// The entity that had a component of type <typeparamref name="T"/> enabled.
        /// </summary>
        public Entity Entity;
    }

    /// <summary>
    /// Message emitted after a component has been disabled.
    /// </summary>
    /// <seealso cref="IMessage" />
    public struct OnDisable : IComponentMessage
    {
        /// <summary>
        /// The entity that had a component disabled.
        /// </summary>
        public Entity Entity;
        /// <summary>
        /// The component type that was disabled.
        /// </summary>
        public Metadata Component;
    }

    /// <summary>
    /// Message emitted after a component of type <typeparamref name="T"/> has been disabled.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <seealso cref="IMessage" />
    public struct OnDisable<T> : IComponentMessage where T : struct, IComponent
    {
        /// <summary>
        /// The entity that had a component of type <typeparamref name="T"/> disabled.
        /// </summary>
        public Entity Entity;
    }

    namespace Segment
    {
        /// <summary>
        /// Message emitted after a <see cref="Modules.Component.Segment"/> is created.
        /// </summary>
        /// <seealso cref="IMessage" />
        public struct OnCreate : IMessage
        {
            /// <summary>
            /// The created segment.
            /// </summary>
            public Modules.Component.Segment Segment;
        }

        /// <summary>
        /// Message emitted after an entity has been moved.
        /// </summary>
        /// <seealso cref="IMessage" />
        public struct OnMove : IMessage
        {
            /// <summary>
            /// The moved entity.
            /// </summary>
            public Entity Entity;
            /// <summary>
            /// The source of the move.
            /// </summary>
            public (Modules.Component.Segment segment, int index) Source;
            /// <summary>
            /// The target of the move.
            /// </summary>
            public (Modules.Component.Segment segment, int index) Target;
        }
    }
}