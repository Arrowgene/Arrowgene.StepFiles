﻿using System;

namespace Arrowgene.StepFile.Gui.Core.DynamicGridView
{
    public class DynamicGridViewColumn
    {
        public string Header { get; set; }
        public string TextField { get; set; }
        public string ContentField { get; set; }
        public double Width { get; set; }

        public DynamicGridViewColumn()
        {
            Width = Double.NaN;
        }
    }
}
