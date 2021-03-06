Imports System.IO
Imports System.Runtime.Serialization.Formatters.Binary
Imports System.Linq

'    Copyright 2008 Daniel Wagner O. de Medeiros
'
'    This file is part of DWSIM.
'
'    DWSIM is free software: you can redistribute it and/or modify
'    it under the terms of the GNU General Public License as published by
'    the Free Software Foundation, either version 3 of the License, or
'    (at your option) any later version.
'
'    DWSIM is distributed in the hope that it will be useful,
'    but WITHOUT ANY WARRANTY; without even the implied warranty of
'    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
'    GNU General Public License for more details.
'
'    You should have received a copy of the GNU General Public License
'    along with DWSIM.  If not, see <http://www.gnu.org/licenses/>.

Public Class FormWelcome

    Dim index As Integer = 0

    Private Sub FormTips_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load

        If DWSIM.App.IsRunningOnMono Then Me.BackgroundImageLayout = ImageLayout.Stretch

        Dim existingfiles As New List(Of String)
        For Each f As String In My.Settings.MostRecentFiles
            existingfiles.Add(f)
        Next

        existingfiles = existingfiles.Where(Function(x) File.Exists(x)).OrderByDescending(Function(x) File.GetLastWriteTime(x)).ToList

        For Each f As String In existingfiles
            If Path.GetExtension(f).ToLower <> ".dwbcs" Then
                Me.lvlatest.Items.Add(Path.GetFileName(f), 0).Tag = f
                Dim lvi = Me.lvlatest.Items(Me.lvlatest.Items.Count - 1)
                lvi.ToolTipText = f
                Select Case Path.GetExtension(f).ToLower
                    Case ".dwsim"
                        lvi.ImageIndex = 0
                    Case ".dwxml", ".dwxmz"
                        lvi.ImageIndex = 1
                    Case ".dwcsd"
                        lvi.ImageIndex = 2
                    Case ".dwrsd"
                        lvi.ImageIndex = 3
                End Select
                If Not Me.lvlatestfolders.Items.ContainsKey(Path.GetDirectoryName(f)) Then
                    Me.lvlatestfolders.Items.Add(Path.GetDirectoryName(f), Path.GetDirectoryName(f), 4).Tag = Path.GetDirectoryName(f)
                    Me.lvlatestfolders.Items(Me.lvlatestfolders.Items.Count - 1).ToolTipText = Path.GetDirectoryName(f)
                End If
            End If
        Next

        Dim samples = Directory.EnumerateFiles(My.Application.Info.DirectoryPath & Path.DirectorySeparatorChar & "samples", "*.dw*", SearchOption.TopDirectoryOnly)

        For Each f As String In samples
            Me.lvsamples.Items.Add(Path.GetFileName(f), 0).Tag = f
            Dim lvi = Me.lvsamples.Items(Me.lvsamples.Items.Count - 1)
            lvi.ToolTipText = f
            Select Case Path.GetExtension(f).ToLower
                Case ".dwsim"
                    lvi.ImageIndex = 0
                Case ".dwxml", ".dwxmz"
                    lvi.ImageIndex = 1
                Case ".dwcsd"
                    lvi.ImageIndex = 2
                Case ".dwrsd"
                    lvi.ImageIndex = 3
            End Select
        Next

        If DWSIM.App.IsRunningOnMono Then
            Me.lvlatest.View = View.List
            Me.lvlatestfolders.View = View.List
        End If

    End Sub

    Private Sub KryptonButton5_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button2.Click

        Me.Hide()
        Me.Close()
        Application.DoEvents()
        Application.DoEvents()
        FormMain.NewToolStripButton_Click(sender, e)

    End Sub

    Private Sub KryptonButton4_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button1.Click
        Me.Hide()
        Me.Close()
        Application.DoEvents()
        Application.DoEvents()
        Call FormMain.LoadFileDialog()
    End Sub

    Private Sub lvlatest_ItemActivate(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles lvlatest.ItemActivate, lvsamples.ItemActivate

        Dim lview = DirectCast(sender, ListView)


        If File.Exists(lview.SelectedItems(0).Tag) Then

            Me.Hide()

            Dim floading As New FormLoadingSimulation

            floading.Label1.Text = DWSIM.App.GetLocalString("LoadingFile") & vbCrLf & "(" & lview.SelectedItems(0).Tag.ToString & ")"
            floading.Show()

            Application.DoEvents()

            FormMain.filename = lview.SelectedItems(0).Tag
            Select Case Path.GetExtension(lview.SelectedItems(0).Tag).ToLower
                Case ".dwxml"
                    'FormMain.ToolStripStatusLabel1.Text = DWSIM.App.GetLocalString("Abrindosimulao") + " " + lview.SelectedItems(0).Tag + "..."
                    Application.DoEvents()
                    Application.DoEvents()
                    FormMain.LoadXML(lview.SelectedItems(0).Tag, Sub(x)
                                                                     Me.Invoke(Sub() floading.ProgressBar1.Value = x)
                                                                 End Sub)
                Case ".dwxmz"
                    'FormMain.ToolStripStatusLabel1.Text = DWSIM.App.GetLocalString("Abrindosimulao") + " " + lview.SelectedItems(0).Tag + "..."
                    Application.DoEvents()
                    Application.DoEvents()
                    FormMain.LoadAndExtractXMLZIP(lview.SelectedItems(0).Tag, Sub(x)
                                                                                  Me.Invoke(Sub() floading.ProgressBar1.Value = x)
                                                                              End Sub)
                Case ".dwsim"
                    'FormMain.ToolStripStatusLabel1.Text = DWSIM.App.GetLocalString("Abrindosimulao") + " " + lview.SelectedItems(0).Tag + "..."
                    'Application.DoEvents()
                    'Application.DoEvents()
                    'FormMain.LoadF(lview.SelectedItems(0).Tag)
                Case ".xml"
                    'FormMain.ToolStripStatusLabel1.Text = DWSIM.App.GetLocalString("Abrindosimulao") + " " + lview.SelectedItems(0).Tag + "..."
                    Application.DoEvents()
                    Application.DoEvents()
                    FormMain.LoadMobileXML(lview.SelectedItems(0).Tag)
                Case ".dwcsd"
                    Dim NewMDIChild As New FormCompoundCreator()
                    NewMDIChild.MdiParent = FormMain
                    NewMDIChild.Show()
                    Dim objStreamReader As New FileStream(lview.SelectedItems(0).Tag, FileMode.Open, FileAccess.Read)
                    Dim x As New BinaryFormatter()
                    NewMDIChild.mycase = x.Deserialize(objStreamReader)
                    NewMDIChild.mycase.Filename = lview.SelectedItems(0).Tag
                    objStreamReader.Close()
                    NewMDIChild.WriteData()
                    NewMDIChild.Activate()
                Case ".dwrsd"
                    Dim NewMDIChild As New FormDataRegression()
                    NewMDIChild.MdiParent = Me.Owner
                    NewMDIChild.Show()
                    Dim objStreamReader As New FileStream(lview.SelectedItems(0).Tag, FileMode.Open, FileAccess.Read)
                    Dim x As New BinaryFormatter()
                    NewMDIChild.currcase = x.Deserialize(objStreamReader)
                    NewMDIChild.currcase.filename = lview.SelectedItems(0).Tag
                    objStreamReader.Close()
                    NewMDIChild.LoadCase(NewMDIChild.currcase, False)
                    NewMDIChild.Activate()
                Case ".dwruf"
                    Dim NewMDIChild As New FormUNIFACRegression()
                    NewMDIChild.MdiParent = Me.Owner
                    NewMDIChild.Show()
                    Dim objStreamReader As New FileStream(lview.SelectedItems(0).Tag, FileMode.Open, FileAccess.Read)
                    Dim x As New BinaryFormatter()
                    NewMDIChild.mycase = x.Deserialize(objStreamReader)
                    NewMDIChild.mycase.Filename = lview.SelectedItems(0).Tag
                    objStreamReader.Close()
                    NewMDIChild.LoadCase(NewMDIChild.mycase, False)
                    NewMDIChild.Activate()
            End Select

            floading.Close()

            Me.Close()

        Else

            Throw New FileNotFoundException("File not found.", lview.SelectedItems(0).Tag.ToString)

        End If

    End Sub

    Private Sub lvlatestfolders_ItemActivate(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles lvlatestfolders.ItemActivate

        Me.Close()
        Application.DoEvents()
        Application.DoEvents()
        FormMain.OpenFileDialog1.InitialDirectory = Me.lvlatestfolders.SelectedItems(0).Tag
        Call FormMain.LoadFileDialog()

    End Sub

    Private Sub Button3_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button3.Click
        Dim NewMDIChild As New FormCompoundCreator()
        'Set the Parent Form of the Child window.
        NewMDIChild.MdiParent = Me.Owner
        'Display the new form.
        NewMDIChild.Text = "CompoundCreator" & FormMain.m_childcount
        Me.Hide()
        Me.Close()
        Application.DoEvents()
        Application.DoEvents()
        NewMDIChild.Show()
        NewMDIChild.MdiParent = Me.Owner
    End Sub

    Private Sub Button5_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button5.Click
        Dim NewMDIChild As New FormDataRegression()
        'Set the Parent Form of the Child window.
        NewMDIChild.MdiParent = Me.Owner
        'Display the new form.
        NewMDIChild.Text = "DataRegression" & FormMain.m_childcount
        Me.Hide()
        Me.Close()
        Application.DoEvents()
        Application.DoEvents()
        NewMDIChild.Show()
        NewMDIChild.MdiParent = Me.Owner
    End Sub

    Private Sub Button8_Click(sender As System.Object, e As System.EventArgs) Handles Button8.Click
        Process.Start("https://sourceforge.net/p/dwsim/donate/?source=navbar")
    End Sub

    Protected Overrides Sub OnPaint(ByVal e As System.Windows.Forms.PaintEventArgs)
        ' Do nothing here!
    End Sub

    'Protected Overrides Sub OnPaintBackground(ByVal pevent As System.Windows.Forms.PaintEventArgs)

    '    pevent.Graphics.DrawImage(My.Resources.splashWelcome_Background, New Rectangle(0, 0, Me.Width, Me.Height))

    'End Sub

    Private Sub Button12_Click(sender As Object, e As EventArgs) Handles Button12.Click
        Process.Start("https://itunes.apple.com/us/app/dwsim-simulator/id1162110266?ls=1&mt=8")
    End Sub

    Private Sub Button11_Click(sender As Object, e As EventArgs) Handles Button11.Click
        Process.Start("https://play.google.com/store/apps/details?id=com.danielmedeiros.dwsim_simulator")
    End Sub

    Private Sub LinkLabel4_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) Handles LinkLabel4.LinkClicked
        Process.Start("http://dwsim.inforside.com.br/wiki/index.php?title=Main_Page")
    End Sub

    Private Sub LinkLabel5_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) Handles LinkLabel5.LinkClicked
        Process.Start("https://sourceforge.net/p/dwsim/discussion/?source=navbar")
    End Sub

    Private Sub LinkLabel1_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) Handles LinkLabel1.LinkClicked
        Process.Start("https://www.youtube.com/channel/UCzzBQrycKoN5XbCeLV12y3Q")
    End Sub

    Private Sub LinkLabel2_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) Handles LinkLabel2.LinkClicked
        Process.Start("http://dwsim.inforside.com.br/wiki/index.php?title=Category:Tutorials")
    End Sub

    Private Sub LinkLabel3_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) Handles LinkLabel3.LinkClicked
        Process.Start("http://dwsim.fossee.in/flowsheeting-project/completed-flowsheet")
    End Sub
End Class