using System;

namespace G19USB
{
    /// <summary>Active macro bank selected by M1/M2/M3.</summary>
    public enum GKeyBank
    {
        /// <summary>No M-key bank selected (default).</summary>
        Default,
        /// <summary>M1 macro bank.</summary>
        M1,
        /// <summary>M2 macro bank.</summary>
        M2,
        /// <summary>M3 macro bank.</summary>
        M3,
    }

    /// <summary>
    /// The special keys of the G19 keyboard.
    /// Based on libg19: https://github.com/jgeboski/libg19
    /// </summary>
    [Flags]
    public enum G19Keys : uint
    {
        /// <summary>No keys pressed.</summary>
        None = 0,

        // L-Keys (LCD navigation keys)
        /// <summary>L-Home navigation key.</summary>
        LHome = 1 << 0,
        /// <summary>L-Cancel navigation key.</summary>
        LCancel = 1 << 1,
        /// <summary>L-Menu navigation key.</summary>
        LMenu = 1 << 2,
        /// <summary>L-Ok navigation key.</summary>
        LOk = 1 << 3,
        /// <summary>L-Right navigation key.</summary>
        LRight = 1 << 4,
        /// <summary>L-Left navigation key.</summary>
        LLeft = 1 << 5,
        /// <summary>L-Down navigation key.</summary>
        LDown = 1 << 6,
        /// <summary>L-Up navigation key.</summary>
        LUp = 1 << 7,

        // G-Keys (programmable macro keys)
        /// <summary>Programmable G1 key.</summary>
        G1 = 1 << 8,
        /// <summary>Programmable G2 key.</summary>
        G2 = 1 << 9,
        /// <summary>Programmable G3 key.</summary>
        G3 = 1 << 10,
        /// <summary>Programmable G4 key.</summary>
        G4 = 1 << 11,
        /// <summary>Programmable G5 key.</summary>
        G5 = 1 << 12,
        /// <summary>Programmable G6 key.</summary>
        G6 = 1 << 13,
        /// <summary>Programmable G7 key.</summary>
        G7 = 1 << 14,
        /// <summary>Programmable G8 key.</summary>
        G8 = 1 << 15,
        /// <summary>Programmable G9 key.</summary>
        G9 = 1 << 16,
        /// <summary>Programmable G10 key.</summary>
        G10 = 1 << 17,
        /// <summary>Programmable G11 key.</summary>
        G11 = 1 << 18,
        /// <summary>Programmable G12 key.</summary>
        G12 = 1 << 19,

        // M-Keys (mode keys)
        /// <summary>M1 macro bank key.</summary>
        M1 = 1 << 20,
        /// <summary>M2 macro bank key.</summary>
        M2 = 1 << 21,
        /// <summary>M3 macro bank key.</summary>
        M3 = 1 << 22,
        /// <summary>MR (Macro Record) bank key.</summary>
        MR = 1 << 23  // MR (Macro Record) key
    }
}
