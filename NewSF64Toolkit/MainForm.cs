﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System.Design;

namespace NewSF64Toolkit
{
    public partial class MainForm : Form
    {
        private const string STATUS_NO_FILE_LOADED = "No file loaded";
        private const string STATUS_FILE_LOADED = "ROM file loaded";

        private string[] VALID_ROM_EXTENSIONS = { ".ROM", ".Z64", ".N64" };

        private ROMFile _rom;

        private OpenGLControl _glControl;
        private ByteViewer _byteViewer;
        private F3DEXParser _parser;
        private StarFoxLevelLoader _levelLoader;
        private StarFoxModelLoader _modelLoader;

        public MainForm()
        {
            InitializeComponent();

            tsStatus.Text = STATUS_NO_FILE_LOADED;

            _glControl = new OpenGLControl();
            this.glPanel.Controls.Add(_glControl);
            _glControl.Dock = DockStyle.Fill;
            _glControl.Visible = false;

            _byteViewer = new ByteViewer();
            this.glPanel.Controls.Add(_byteViewer);
            _byteViewer.Visible = false;
            //_byteViewer.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            //_byteViewer.Location = new Point(0, 0);
            //_byteViewer.Size = glPanel.Size;
            _byteViewer.Dock = DockStyle.Fill;

            _parser = new F3DEXParser(_glControl);
            _levelLoader = new StarFoxLevelLoader(_parser);
            _modelLoader = new StarFoxModelLoader(_parser);

            cbLevelSelect.SelectedIndex = 0;
        }

        #region Event handlers

        private void menuStripFileLoad_Click(object sender, EventArgs e)
        {
            openFileDialog.FileName = "sf64.z64";
            openFileDialog.DefaultExt = "";

            //Load the rom here
            if (openFileDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            
            string romFile = openFileDialog.FileName;

            if (!File.Exists(romFile))
            {
                //Error message
                MessageBox.Show("File not found, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!HasRomExtension(romFile))
            {
                //Error message
                MessageBox.Show("Specified file is not a rom file, please check the extension and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            //Load it here
            byte[] data = null;
            try
            {
                data = File.ReadAllBytes(romFile);
            }
            catch
            {
                MessageBox.Show("Unable to load the file, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string fileName = Path.GetFileName(romFile);

            saveFileDialog.DefaultExt = Path.GetExtension(romFile);
            saveFileDialog.FileName = fileName;


            if (data != null && data.Length > 64)
            {
                if (_rom != null)
                    _rom.ClearROMData();

                _rom = new ROMFile(fileName, data);

                //This will happen if it did not find an appropriate GameID/Version match
                if (!_rom.IsValidRom)
                {
                    MessageBox.Show("Unable to identify the ROM, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _rom.ClearROMData();
                    return;
                }

                //Here we need to start loading the DMA table and giving feedback to the user about the success
                tsStatus.Text = STATUS_FILE_LOADED;

                //We need a way to discriminate the endianess of the system, and keep the data
                //    right side forward. EDIT: Endianness is described at header of ROM file, see
                //    http://www.emutalk.net/archive/index.php/t-16045.html
                RefreshROMInfo();

                RefreshDMATable();
            }
        }

        private void menuStripViewHex_Click(object sender, EventArgs e)
        {
            ToolSettings.DisplayInHex = menuStripViewHex.Checked;

            RefreshROMInfo();

            RefreshDMATable();
        }

        private void menuStripFileSave_Click(object sender, EventArgs e)
        {
            if (_rom == null || !_rom.IsROMLoaded || !_rom.IsValidRom)
            {
                //Error message
                MessageBox.Show("No valid ROM file loaded currently, please load a ROM and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
            {
                writer.BaseStream.Write(_rom.RomData, 0, _rom.RomData.Length);
            }

        }

        private void menuStripFileLoadDMA_Click(object sender, EventArgs e)
        {
            //Load up DMA tables directly
            openFileDialog.FileName = "layout.txt";
            openFileDialog.DefaultExt = ".txt";

            if(openFileDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            if(!File.Exists(openFileDialog.FileName))
            {
                //Error message
                MessageBox.Show("File not found, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            uint fileSize = 0;
            List<string> dmaFileNames = new List<string>();
            string fileName;

            using(StreamReader reader = new StreamReader(openFileDialog.FileName))
            {
                string firstLine = reader.ReadLine();

                string length = firstLine.Substring(0, 8);
                fileSize = Convert.ToUInt32(length, 16);
                fileName = firstLine.Split(' ')[1];

                while(!reader.EndOfStream)
                {
                    dmaFileNames.Add(reader.ReadLine());
                }
            }

            if(dmaFileNames.Count == 0 || fileSize == 0)
            {
                //Error message
                MessageBox.Show("Incorrect layout format, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<byte[]> dmaEntries = new List<byte[]>();

            string directory = Path.GetDirectoryName(openFileDialog.FileName);

            foreach(string dmaFilename in dmaFileNames)
            {
                string fullDmaPath = Path.Combine(directory, dmaFilename);

                if(!File.Exists(fullDmaPath))
                {
                    //Error message
                    MessageBox.Show("DMA entry not found, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using(StreamReader reader = new StreamReader(fullDmaPath))
                {
                    byte[] bytes = new byte[reader.BaseStream.Length];

                    reader.BaseStream.Read(bytes, 0, (int)reader.BaseStream.Length);

                    dmaEntries.Add(bytes);
                }
            }

            _rom = new ROMFile(fileName, dmaEntries);

            //This will happen if it did not find an appropriate GameID/Version match
            if (!_rom.IsValidRom)
            {
                MessageBox.Show("Unable to identify the ROM, please try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _rom.ClearROMData();
                return;
            }

            tsStatus.Text = STATUS_FILE_LOADED;

            RefreshROMInfo();

            RefreshDMATable();
        }

        private void menuStripFileSaveDMA_Click(object sender, EventArgs e)
        {
            //Save DMA tables directly
            
            if (_rom == null || !_rom.IsROMLoaded || !_rom.IsValidRom)
            {
                //Error message
                MessageBox.Show("No valid ROM file loaded currently, please load a ROM and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if(!_rom.IsDMALoaded)
            {
                //Error message
                MessageBox.Show("Error with DMA tables, try reloading the ROM.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
            }

            if(folderBrowserDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string outputFolderPath = folderBrowserDialog.SelectedPath;

            if(!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            string layoutFilePath = Path.Combine(outputFolderPath, "layout.txt");

            List<string> layoutText = new List<string>();
            int totalDMALength = 0x0;

            layoutText.Add("00000000 rebuilt.z64");

            for(int i = 0; i < _rom.DMATable.Count; i++)
            {
                DMATableEntry dma = _rom.DMATable[i];

                //Need to append filepath!!!!
                string fileName = string.Format("{0:00}_{1:X8}-{2:X8}_vs{3:X8}.{4}", i, dma.PStart, dma.PEnd, dma.VStart, (dma.CompFlag == 0x1 ? "mio" : "bin"));

                layoutText.Add(string.Format("{0:00}_{1:X8}-{2:X8}_vs{3:X8}.bin", i, dma.PStart, dma.PEnd, dma.VStart));

                //Write file
                string outputFile = Path.Combine(outputFolderPath, fileName);
                using(StreamWriter writer = new StreamWriter(outputFile))
                {
                    writer.BaseStream.Write(dma.DMAData, 0, dma.DMAData.Length);
                }
                
                //if compressed, decompress and make new file
                if(dma.CompFlag == 0x1)
                {
                    byte[] decompressedData = null;
                    if(ToolSettings.DecompressMIO0(dma.DMAData, out decompressedData))
                    {
                        fileName = Path.ChangeExtension(fileName, "bin");
                        outputFile = Path.Combine(outputFolderPath, fileName);
                        using(StreamWriter writer = new StreamWriter(outputFile))
                        {
                            writer.BaseStream.Write(decompressedData, 0, decompressedData.Length);
                        }

                        totalDMALength += decompressedData.Length;
                    }
                }
                else
                {
                    totalDMALength += dma.DMAData.Length;
                }
            }

            totalDMALength = (totalDMALength / 0x400000 + 1) * 0x400000;

            layoutText[0] = string.Format("{0:X8} rebuilt.z64", totalDMALength);

            using(StreamWriter writer = new StreamWriter(layoutFilePath))
            {
                foreach(string text in layoutText)
                    writer.WriteLine(text);
            }

        }

        private void menuStripROMFixCRCs_Click(object sender, EventArgs e)
        {
            _rom.FixCRC();

            RefreshROMInfo();
        }

        private void menuStripROMDecompress_Click(object sender, EventArgs e)
        {
            _rom.Decompress();

            RefreshROMInfo();
            RefreshDMATable();
        }

        private void dgvDMA_SelectionChanged(object sender, EventArgs e)
        {
            if (dgvDMA.SelectedCells.Count == 1)
            {
                _byteViewer.SetBytes(_rom.DMATable[dgvDMA.SelectedCells[0].RowIndex].DMAData);
            }
        }

        private void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl.SelectedIndex == 0) //Rom info
            {
                _glControl.Visible = false;
                _byteViewer.Visible = false;
                _aboutControl.Visible = true;
            }
            else if (tabControl.SelectedIndex == 1) //DMA tables
            {
                _glControl.Visible = false;
                _byteViewer.Visible = true;
                _aboutControl.Visible = false;
            }
            else
            {
                _glControl.Visible = true;
                _byteViewer.Visible = false;
                _aboutControl.Visible = false;
            }
        }

        private void cbLevelSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            //Include index 4?
            if (cbLevelSelect.SelectedIndex == 11 || cbLevelSelect.SelectedIndex == 12 ||
                cbLevelSelect.SelectedIndex == 13 || cbLevelSelect.SelectedIndex == 15)
            {
                btnLoadLevel.Enabled = false;
            }
            else
                btnLoadLevel.Enabled = true;
        }

        private void btnLoadLevel_Click(object sender, EventArgs e)
        {
            int levelDMAIndex = GetLevelDMAIndex();

            if (!_rom.IsDMALoaded || _rom.DMATable.Count <= levelDMAIndex)
            {
                //Error message
                MessageBox.Show("Rom file not loaded correctly, try reloading the ROM.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_rom.DMATable[levelDMAIndex].CompFlag == 0x01)
            {
                //Error message
                MessageBox.Show("Specified level file is compressed, decompress before trying again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            //Initiate the level loading. Grab the correct offset info and pass it to the F3DEX parser
            DMATableEntry offsetTableDMA = _rom.DMATable[1];
            _parser.AddBank((byte)0xFF, offsetTableDMA.DMAData, (uint)0x0);

            uint offset = ToolSettings.ReadUInt(offsetTableDMA.DMAData, 0xCE158 + cbLevelSelect.SelectedIndex * 0x04);
            byte segment = (byte)((offset & 0xFF000000) >> 24);
            offset &= 0x00FFFFFF;

            //_glControl.Clear();
            _parser.AddBank(segment, _rom.DMATable[levelDMAIndex].DMAData, 0x00);

            _levelLoader.StartReadingLevelDataAt(segment, offset);

            InitDListNavigEnabled(true);

            _glControl.ReDraww();
        }

        #endregion

        #region Private methods

        private bool HasRomExtension(string fileName)
        {
            string ext = Path.GetExtension(fileName.ToUpper());

            return VALID_ROM_EXTENSIONS.Contains(ext);
        }

        private void RefreshROMInfo()
        {
            txtFilename.Text = _rom.Filename;
            txtSize.Text = ToolSettings.DisplayValue(_rom.Size);
            txtTitle.Text = _rom.Info.Title;
            txtGameID.Text = _rom.Info.GameID;
            txtVersion.Text = _rom.Info.Version.ToString();
            txtCRC1.Text = ToolSettings.DisplayValue(_rom.Info.CRC1);
            txtCRC2.Text = ToolSettings.DisplayValue(_rom.Info.CRC2);
        }

        private void RefreshDMATable()
        {
            dgvDMA.Rows.Clear();

            for (int i = 0; i < _rom.DMATable.Count; i++)
            {
                DMATableEntry entry = _rom.DMATable[i];

                dgvDMA.Rows.Add();
                dgvDMA.Rows[dgvDMA.Rows.Count - 1].Cells[0].Value = i + 1;
                dgvDMA.Rows[dgvDMA.Rows.Count - 1].Cells[1].Value = ToolSettings.DisplayValue(entry.VStart);
                dgvDMA.Rows[dgvDMA.Rows.Count - 1].Cells[2].Value = ToolSettings.DisplayValue(entry.PStart);
                dgvDMA.Rows[dgvDMA.Rows.Count - 1].Cells[3].Value = ToolSettings.DisplayValue(entry.PEnd);
            }
        }

        private int GetLevelDMAIndex()
        {
            switch (cbLevelSelect.SelectedIndex)
            {
                case 0:
                    return 18;
                case 1:
                    return 19;
                case 2:
                    return 26;
                case 3:
                    return 29;
                case 4:
                    return 29;
                case 5:
                    return 35;
                case 6:
                    return 30;
                case 7:
                    return 36;
                case 8:
                    return 37;
                case 9:
                    return 47;
                case 10:
                    return 53;
                case 11:
                    return -1;
                case 12:
                    return -1;
                case 13:
                    return -1;
                case 14:
                    return 34;
                case 15:
                    return -1;
                case 16:
                    return 38;
                case 17:
                    return 33;
                case 18:
                    return 27;
                case 19:
                    return 31;
                case 20:
                    return 12;
                default:
                    return -1;
            }
        }

        #endregion

        private void InitDListNavigEnabled(bool enable)
        {
            btnModLeft.Enabled = enable;
            btnModRight.Enabled = enable;
            btnModSnapTo.Enabled = enable;

            if (!enable)
            {
                txtModDList.Clear();
                txtModID.Clear();
                txtModNum.Clear();
                txtModPos.Clear();
                txtModUnk.Clear();
                txtModX.Clear();
                txtModXRot.Clear();
                txtModY.Clear();
                txtModYRot.Clear();
                txtModZ.Clear();
                txtModZRot.Clear();
            }
            else
            {
                SFGfx.SelectedGameObject = 0;
                LoadModelNavigInfo();
            }
        }

        private void LoadModelNavigInfo()
        {
            txtModNum.Text = SFGfx.SelectedGameObject.ToString();

            StarFoxLevelLoader.GameObject obj = _levelLoader.GameObjects[SFGfx.SelectedGameObject];


            txtModDList.Text = ToolSettings.DisplayValue(obj.DListOffset);
            txtModID.Text = obj.ID.ToString();
            txtModPos.Text = obj.LvlPos.ToString();
            txtModUnk.Text = obj.Unk.ToString();
            txtModX.Text = obj.X.ToString();
            txtModXRot.Text = obj.XRot.ToString();
            txtModY.Text = obj.Y.ToString();
            txtModYRot.Text = obj.YRot.ToString();
            txtModZ.Text = obj.Z.ToString();
            txtModZRot.Text = obj.ZRot.ToString();

        }

        private void btnModRight_Click(object sender, EventArgs e)
        {
            if (SFGfx.SelectedGameObject < SFGfx.GameObjCount - 1)
            {
                SFGfx.SelectedGameObject++;
                LoadModelNavigInfo();
                _glControl.ReDraww();
            }
        }

        private void btnModLeft_Click(object sender, EventArgs e)
        {
            if (SFGfx.SelectedGameObject > 0)
            {
                SFGfx.SelectedGameObject--;
                LoadModelNavigInfo();
                _glControl.ReDraww();
            }
        }

        private void btnModSnapTo_Click(object sender, EventArgs e)
        {
            //Move the camera to the object
            StarFoxLevelLoader.GameObject obj = _levelLoader.GameObjects[SFGfx.SelectedGameObject];

            SFCamera.MoveCameraTo((float)obj.X, (float)obj.Y, (float)obj.Z - obj.LvlPos);
        }
    }
}
