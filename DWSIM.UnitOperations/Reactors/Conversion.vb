'    Conversion Reactor Calculation Routines 
'    Copyright 2008-2018 Daniel Wagner O. de Medeiros
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

Imports DWSIM.DrawingTools.GraphicObjects
Imports DWSIM.Thermodynamics.BaseClasses
Imports Ciloci.Flee
Imports System.Math
Imports System.Linq
Imports DWSIM.Interfaces.Enums
Imports DWSIM.SharedClasses
Imports DWSIM.Thermodynamics.Streams
Imports DWSIM.Thermodynamics

Imports DotNumerics.Optimization

Namespace Reactors

    <System.Serializable()> Public Class Reactor_Conversion

        Inherits Reactor

        <NonSerialized> <Xml.Serialization.XmlIgnore> Dim f As EditingForm_ReactorConvEqGibbs

        Public Sub New()
            MyBase.New()
        End Sub

        Public Sub New(ByVal name As String, ByVal description As String)

            MyBase.New()
            Me.ComponentName = name
            Me.ComponentDescription = description

        End Sub

        Public Overrides Function CloneXML() As Object
            Return New Reactor_Conversion().LoadData(Me.SaveData)
        End Function

        Public Overrides Function CloneJSON() As Object
            Return Newtonsoft.Json.JsonConvert.DeserializeObject(Of Reactor_Conversion)(Newtonsoft.Json.JsonConvert.SerializeObject(Me))
        End Function

        Public Overrides Sub Validate()

            If Not Me.GraphicObject.InputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac16"))
            ElseIf Not Me.GraphicObject.OutputConnectors(0).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac15"))
            ElseIf Not Me.GraphicObject.OutputConnectors(1).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedematriac15"))
            ElseIf Not Me.GraphicObject.InputConnectors(1).IsAttached Then
                Throw New Exception(FlowSheet.GetTranslatedString("Nohcorrentedeenerg17"))
            End If

            If Conversions Is Nothing Then m_conversions = New Dictionary(Of String, Double)

        End Sub

        Private Sub InitVars()

            Me.Reactions.Clear()
            Me.ReactionsSequence.Clear()
            Me.Conversions.Clear()
            Me.DeltaQ = 0
            Me.DeltaT = 0

            'check active reactions (conversion only) in the reaction set
            For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                If FlowSheet.Reactions(rxnsb.ReactionID).ReactionType = ReactionType.Conversion And rxnsb.IsActive Then
                    Me.Reactions.Add(rxnsb.ReactionID)
                End If
            Next

            'order reactions
            Dim i As Integer
            i = 0
            Dim maxrank As Integer = 0
            For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                If rxnsb.Rank > maxrank And Me.Reactions.Contains(rxnsb.ReactionID) Then maxrank = rxnsb.Rank
            Next

            'ordering of parallel reactions

            i = 0
            Dim arr As List(Of String)
            Do
                arr = New List(Of String)
                For Each rxnsb As ReactionSetBase In FlowSheet.ReactionSets(Me.ReactionSetID).Reactions.Values
                    If rxnsb.Rank = i And Me.Reactions.Contains(rxnsb.ReactionID) Then arr.Add(rxnsb.ReactionID)
                Next
                If arr.Count > 0 Then Me.ReactionsSequence.Add(arr)
                i = i + 1
            Loop Until i = maxrank + 1

        End Sub

        Public Overrides Sub Calculate(Optional ByVal args As Object = Nothing)

            Validate()

            InitVars()

            Dim ims As MaterialStream = DirectCast(FlowSheet.SimulationObjects(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name), MaterialStream).Clone
            Dim pp = Me.PropertyPackage

            ims.SetFlowsheet(Me.FlowSheet)
            ims.PreferredFlashAlgorithmTag = Me.PreferredFlashAlgorithmTag

            pp.CurrentMaterialStream = ims

            Dim DN As New Dictionary(Of String, Double)
            Dim N0 As New Dictionary(Of String, Double)
            Dim N As New Dictionary(Of String, Double)

            Dim X, scBC, nBC, DHr, Hid_r, Hid_p, Hr, Hp, Tin, Pin, Pout, W As Double
            Dim i As Integer
            Dim BC As String = ""

            Dim tmp As IFlashCalculationResult

            Tin = ims.Phases(0).Properties.temperature.GetValueOrDefault
            Pin = ims.Phases(0).Properties.pressure.GetValueOrDefault
            W = ims.Phases(0).Properties.massflow.GetValueOrDefault
            Pout = ims.Phases(0).Properties.pressure.GetValueOrDefault - Me.DeltaP.GetValueOrDefault
            ims.Phases(0).Properties.pressure = Pout

            Dim ni0(N.Count - 1), nif(N.Count - 1) As Double

            Dim cnames = ims.PropertyPackage.RET_VNAMES().ToList()

            ni0 = ims.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Mixture).MultiplyConstY(ims.Phases(0).Properties.molarflow.GetValueOrDefault)

            Dim rxn As Reaction

            'loop through conversion reaction groups (parallel/sequential) as defined in the reaction set

            For Each ar In Me.ReactionsSequence

                'Reactants Enthalpy (kJ/kg * kg/s = kW)

                Hr = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                pp.CurrentMaterialStream = ims

                i = 0
                Do

                    'process reaction i

                    rxn = FlowSheet.Reactions(ar(i))
                    BC = rxn.BaseReactant
                    scBC = rxn.Components(BC).StoichCoeff

                    'initial mole flows
                    For Each sb As ReactionStoichBase In rxn.Components.Values

                        Select Case rxn.ReactionPhase
                            Case PhaseName.Liquid
                                If Not N0.ContainsKey(sb.CompName) Then
                                    N0.Add(sb.CompName, ims.Phases(1).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(1).Properties.molarflow.GetValueOrDefault)
                                    N.Add(sb.CompName, N0(sb.CompName))
                                Else
                                    N0(sb.CompName) = ims.Phases(1).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(1).Properties.molarflow.GetValueOrDefault
                                End If
                            Case PhaseName.Vapor
                                If Not N0.ContainsKey(sb.CompName) Then
                                    N0.Add(sb.CompName, ims.Phases(2).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(2).Properties.molarflow.GetValueOrDefault)
                                    N.Add(sb.CompName, N0(sb.CompName))
                                Else
                                    N0(sb.CompName) = ims.Phases(2).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(2).Properties.molarflow.GetValueOrDefault
                                End If
                            Case PhaseName.Mixture
                                If Not N0.ContainsKey(sb.CompName) Then
                                    N0.Add(sb.CompName, ims.Phases(0).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(0).Properties.molarflow.GetValueOrDefault)
                                    N.Add(sb.CompName, N0(sb.CompName))
                                Else
                                    N0(sb.CompName) = ims.Phases(0).Compounds(sb.CompName).MoleFraction.GetValueOrDefault * ims.Phases(0).Properties.molarflow.GetValueOrDefault
                                End If
                        End Select

                    Next

                    i += 1

                Loop Until i = ar.Count

                ' solve parallel reactions with Simplex NL solver

                ' problem setup: minimize the difference between defined and calculated (final) conversions, subject to Ni >= 0.

                ' this solution scheme for parallel reactions guarantee that the mass balance is preserved, even if the final
                ' conversion values aren't reached due to limited reactant amounts.

                Dim xref(ar.Count - 1), xf(ar.Count - 1), dni(N.Count - 1) As Double
                
                Dim splex As New Simplex()
                Dim vars As New List(Of OptSimplexBoundVariable)

                splex.MaxFunEvaluations = 100000
                splex.Tolerance = 1.0E-20

                i = 0
                Do

                    'check defined conversions

                    rxn = FlowSheet.Reactions(ar(i))

                    rxn.ExpContext = New Ciloci.Flee.ExpressionContext
                    rxn.ExpContext.Imports.AddType(GetType(System.Math))
                    rxn.ExpContext.Variables.Clear()
                    rxn.ExpContext.Options.ParseCulture = Globalization.CultureInfo.InvariantCulture
                    rxn.ExpContext.Variables.Add("T", ims.Phases(0).Properties.temperature.GetValueOrDefault)

                    rxn.Expr = rxn.ExpContext.CompileGeneric(Of Double)(rxn.Expression)
                    X = rxn.Expr.Evaluate / 100

                    If X < 0.0# Or X > 1.0# Then Throw New ArgumentOutOfRangeException("Conversion Expression", "The conversion expression for reaction " & rxn.Name & " results in a value that is out of the valid range (0 to 100%).")

                    'store defined (reference) conversions for simplex solver

                    xref(i) = X

                    'create simplex vars

                    vars.Add(New OptSimplexBoundVariable(0.0#, 0.0#, xref(i)))

                    i += 1

                Loop Until i = ar.Count

                'solve parallel reactions 
                'xf = final conversion values

                xf = splex.ComputeMin(Function(xi)

                                          Dim i2, j2, n2 As Integer, m0 As Double

                                          n2 = xi.Length - 1

                                          Select Case rxn.ReactionPhase
                                              Case PhaseName.Liquid
                                                  m0 = ims.Phases(3).Properties.molarflow.GetValueOrDefault
                                                  nif = ims.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Liquid).MultiplyConstY(m0)
                                              Case PhaseName.Vapor
                                                  m0 = ims.Phases(2).Properties.molarflow.GetValueOrDefault
                                                  nif = ims.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Vapor).MultiplyConstY(m0)
                                              Case PhaseName.Mixture
                                                  m0 = ims.Phases(0).Properties.molarflow.GetValueOrDefault
                                                  nif = ims.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Mixture).MultiplyConstY(m0)
                                          End Select

                                          For i2 = 0 To n2

                                              dni = ims.PropertyPackage.RET_NullVector()

                                              'process reaction i2

                                              rxn = FlowSheet.Reactions(ar(i2))
                                              BC = rxn.BaseReactant
                                              scBC = rxn.Components(BC).StoichCoeff

                                              nBC = N0(rxn.BaseReactant)

                                              'delta mole flows

                                              For Each sb As ReactionStoichBase In rxn.Components.Values
                                                  j2 = cnames.IndexOf(sb.CompName)
                                                  dni(j2) += -xi(i2) * rxn.Components(sb.CompName).StoichCoeff / scBC * nBC
                                              Next

                                              'calculate final mole amounts

                                              For Each sb As ReactionStoichBase In rxn.Components.Values
                                                  j2 = cnames.IndexOf(sb.CompName)
                                                  nif(j2) += dni(j2)
                                              Next

                                          Next

                                          'calculate a penalty value for the objective function due to negative mole flows

                                          Dim pen_val As Double = 0.0#

                                          For Each d In nif
                                              If d < 0.0 Then pen_val += -d * 1.0E+150
                                          Next

                                          Return xref.SubtractY(xi).AbsSqrSumY + pen_val

                                      End Function, vars.ToArray)

                ' at this point, the xf vector holds the final conversion values as calculated 
                ' by the simplex solver, and the energy balance can be calculated (again). 

                i = 0
                DHr = 0
                Hid_r = 0
                Hid_p = 0
                Do

                    'process reaction i
                    rxn = FlowSheet.Reactions(ar(i))
                    BC = rxn.BaseReactant
                    scBC = rxn.Components(BC).StoichCoeff

                    nBC = N0(rxn.BaseReactant)

                    If Not Me.Conversions.ContainsKey(rxn.ID) Then
                        Me.Conversions.Add(rxn.ID, xf(i))
                    Else
                        Me.Conversions(rxn.ID) = xf(i)
                    End If

                    'delta mole flows

                    For Each sb As ReactionStoichBase In rxn.Components.Values
                        If Not DN.ContainsKey(sb.CompName) Then
                            DN.Add(sb.CompName, -xf(i) * rxn.Components(sb.CompName).StoichCoeff / scBC * nBC)
                        Else
                            DN(sb.CompName) = -xf(i) * rxn.Components(sb.CompName).StoichCoeff / scBC * nBC
                        End If
                    Next

                    'final mole flows

                    For Each sb As ReactionStoichBase In rxn.Components.Values
                        N(sb.CompName) += DN(sb.CompName)
                        If N(sb.CompName) < 0.0 Then N(sb.CompName) = 0.0
                    Next

                    'Ideal Gas Reactants Enthalpy (kJ/kg * kg/s = kW)
                    Hid_r += 0 'ppr.RET_Hid(298.15, ims.Phases(0).Properties.temperature.GetValueOrDefault, PropertyPackages.Phase.Mixture) * ims.Phases(0).Properties.massflow.GetValueOrDefault

                    'update mole flows/fractions
                    Dim Nall(ims.Phases(0).Compounds.Count - 1), Nsum As Double
                    For Each sb As ReactionStoichBase In rxn.Components.Values

                        Nsum = 0
                        For Each s2 As Compound In ims.Phases(0).Compounds.Values
                            If rxn.Components.ContainsKey(s2.Name) Then
                                Nsum += N(s2.Name)
                            Else
                                Nsum += ims.Phases(0).Compounds(s2.Name).MoleFraction.GetValueOrDefault * ims.Phases(0).Properties.molarflow.GetValueOrDefault
                            End If
                        Next

                        For Each s3 As Compound In ims.Phases(0).Compounds.Values
                            If rxn.Components.ContainsKey(s3.Name) Then
                                s3.MoleFraction = N(s3.Name) / Nsum
                            Else
                                s3.MoleFraction = ims.Phases(0).Compounds(s3.Name).MoleFraction.GetValueOrDefault * ims.Phases(0).Properties.molarflow.GetValueOrDefault / Nsum
                            End If
                        Next

                        ims.Phases(0).Properties.molarflow = Nsum

                        Dim mmm As Double = 0

                        For Each s3 As Compound In ims.Phases(0).Compounds.Values
                            mmm += s3.MoleFraction.GetValueOrDefault * s3.ConstantProperties.Molar_Weight
                        Next

                        For Each s3 As Compound In ims.Phases(0).Compounds.Values
                            s3.MassFraction = s3.MoleFraction.GetValueOrDefault * s3.ConstantProperties.Molar_Weight / mmm
                        Next

                    Next

                    'Ideal Gas Products Enthalpy (kJ/kg * kg/s = kW)
                    Hid_p += 0 'ppr.RET_Hid(298.15, ims.Phases(0).Properties.temperature.GetValueOrDefault, PropertyPackages.Phase.Mixture) * ims.Phases(0).Properties.massflow.GetValueOrDefault

                    'Heat released (or absorbed) (kJ/s = kW) (Ideal Gas)
                    DHr += rxn.ReactionHeat * Abs(DN(rxn.BaseReactant)) / 1000

                    i += 1

                Loop Until i = ar.Count

                'do a flash calc (calculate final temperature/enthalpy)

                pp.CurrentMaterialStream = ims

                Select Case Me.ReactorOperationMode

                    Case OperationMode.Adiabatic

                        Me.DeltaQ = 0.0#

                        'Products Enthalpy (kJ/kg * kg/s = kW)
                        Hp = Hr + Hid_p - Hid_r - DHr
                        Hp = Hp / W

                        ims.Phases(0).Properties.enthalpy = Hp
                        ims.SpecType = StreamSpec.Pressure_and_Enthalpy

                        ims.Calculate(True, True)

                        Dim Tout As Double = ims.Phases(0).Properties.temperature.GetValueOrDefault
                        Me.DeltaT = Tout - Tin

                    Case OperationMode.Isothermic

                        ims.SpecType = StreamSpec.Temperature_and_Pressure
                        ims.Calculate(True, True)

                        'Products Enthalpy (kJ/kg * kg/s = kW)
                        Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                        'Heat (kW)
                        Me.DeltaQ += DHr + Hid_r - Hr - Hid_p

                        Me.DeltaT = 0

                    Case OperationMode.OutletTemperature

                        Dim Tout As Double = Me.OutletTemperature

                        Me.DeltaT = Tout - Tin

                        ims.Phases(0).Properties.temperature = Tout
                        ims.SpecType = StreamSpec.Temperature_and_Pressure

                        ims.Calculate(True, True)

                        'Products Enthalpy (kJ/kg * kg/s = kW)
                        Hp = ims.Phases(0).Properties.enthalpy.GetValueOrDefault * ims.Phases(0).Properties.massflow.GetValueOrDefault

                        'Heat (kW)
                        Me.DeltaQ = Me.DeltaQ + DHr + Hid_r - Hr - Hid_p

                End Select

            Next

            Select Case Me.ReactorOperationMode

                Case OperationMode.Adiabatic

                    Me.DeltaQ = 0.0#

                Case OperationMode.Isothermic, OperationMode.OutletTemperature

                    Me.DeltaQ += Hp

            End Select

            'Compound conversions

            pp.CurrentMaterialStream = ims

            nif = ims.PropertyPackage.RET_VMOL(PropertyPackages.Phase.Mixture).MultiplyConstY(ims.Phases(0).Properties.molarflow.GetValueOrDefault)

            Me.ComponentConversions.Clear()
            For i = 0 To ni0.Length - 1
                Me.ComponentConversions(cnames(i)) = (ni0(i) - nif(i)) / ni0(i)
            Next

            'Copy results to upstream MS
            Dim xl, xv, xs, T, P, H, S, wtotalx, wtotaly, wtotalS, wl, wv, ws As Double
            Dim nc As Integer = ims.Phases(0).Compounds.Count - 1
            pp.CurrentMaterialStream = ims
            tmp = pp.CalculateEquilibrium2(FlashCalculationType.PressureTemperature, ims.Phases(0).Properties.pressure.GetValueOrDefault, ims.Phases(0).Properties.temperature.GetValueOrDefault, 0)

            Dim Vx(nc), Vy(nc), Vs(nc), Vwx(nc), Vwy(nc), Vws(nc) As Double
            xl = tmp.GetLiquidPhase1MoleFraction
            xv = tmp.GetVaporPhaseMoleFraction
            xs = tmp.GetSolidPhaseMoleFraction
            wl = tmp.GetLiquidPhase1MassFraction
            wv = tmp.GetVaporPhaseMassFraction
            ws = tmp.GetSolidPhaseMassFraction
            T = tmp.CalculatedTemperature.GetValueOrDefault
            P = tmp.CalculatedPressure.GetValueOrDefault
            H = tmp.CalculatedEnthalpy.GetValueOrDefault
            S = tmp.CalculatedEntropy.GetValueOrDefault
            Vx = tmp.GetLiquidPhase1MoleFractions
            Vy = tmp.GetVaporPhaseMoleFractions
            Vs = tmp.GetSolidPhaseMoleFractions

            Dim j As Integer = 0

            Dim ms As MaterialStream
            Dim cp As IConnectionPoint
            cp = Me.GraphicObject.InputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedFrom.Name)
                Dim comp As BaseClasses.Compound
                i = 0
                For Each comp In ms.Phases(0).Compounds.Values
                    wtotalx += Vx(i) * comp.ConstantProperties.Molar_Weight
                    wtotaly += Vy(i) * comp.ConstantProperties.Molar_Weight
                    wtotalS += Vs(i) * comp.ConstantProperties.Molar_Weight
                    i += 1
                Next
                i = 0
                For Each comp In ms.Phases(0).Compounds.Values
                    If wtotalx > 0 Then Vwx(i) = Vx(i) * comp.ConstantProperties.Molar_Weight / wtotalx
                    If wtotaly > 0 Then Vwy(i) = Vy(i) * comp.ConstantProperties.Molar_Weight / wtotaly
                    If wtotalS > 0 Then Vws(i) = Vs(i) * comp.ConstantProperties.Molar_Weight / wtotalS
                    i += 1
                Next
            End If

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .SpecType = StreamSpec.Temperature_and_Pressure
                    .Phases(0).Properties.temperature = T
                    .Phases(0).Properties.pressure = P
                    .Phases(0).Properties.enthalpy = H / wv
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        If xv = 0.0# Then
                            comp.MoleFraction = 0.0#
                            comp.MassFraction = 0.0#
                        Else
                            comp.MoleFraction = Vy(j)
                            comp.MassFraction = Vwy(j)
                        End If
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = W * wv
                    .Phases(0).Properties.massfraction = 1.0#
                    .Phases(0).Properties.molarfraction = 1.0#
                End With
            End If

            cp = Me.GraphicObject.OutputConnectors(1)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .SpecType = StreamSpec.Temperature_and_Pressure
                    .Phases(0).Properties.temperature = T
                    .Phases(0).Properties.pressure = P
                    If wv < 1.0# Then .Phases(0).Properties.enthalpy = H / (1 - wv) Else .Phases(0).Properties.enthalpy = 0.0#
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        If (xl + xs) = 0.0# Then
                            comp.MoleFraction = 0.0#
                            comp.MassFraction = 0.0#
                        Else
                            comp.MoleFraction = (Vx(j) * xl + Vs(j) * xs) / (xl + xs)
                            comp.MassFraction = (Vwx(j) * wtotalx + Vws(j) * wtotalS) / (wtotalx + wtotalS)
                        End If
                        j += 1
                    Next
                    j = 0
                    .Phases(0).Properties.massflow = W * (1 - wv)
                    .Phases(0).Properties.massfraction = 1.0#
                    .Phases(0).Properties.molarfraction = 1.0#
                End With
            End If

            'Corrente de EnergyFlow - atualizar valor da potencia (kJ/s)
            With GetInletEnergyStream(1)
                .EnergyFlow = Me.DeltaQ.GetValueOrDefault
                .GraphicObject.Calculated = True
            End With

        End Sub

        Public Overrides Sub DeCalculate()

            'If Not Me.GraphicObject.InputConnectors(0).IsAttached Then Throw New Exception(Flowsheet.GetTranslatedString("Nohcorrentedematriac10"))
            'If Not Me.GraphicObject.OutputConnectors(0).IsAttached Then Throw New Exception(Flowsheet.GetTranslatedString("Nohcorrentedematriac11"))
            'If Not Me.GraphicObject.OutputConnectors(1).IsAttached Then Throw New Exception(Flowsheet.GetTranslatedString("Nohcorrentedematriac11"))
            'Dim ems As DWSIM.SimulationObjects.Streams.MaterialStream = form.SimulationObjects(Me.GraphicObject.InputConnectors(0).AttachedConnector.AttachedFrom.Name)
            'Dim W As Double = ems.Phases(0).Properties.massflow.GetValueOrDefault
            Dim j As Integer = 0

            Dim ms As MaterialStream
            Dim cp As IConnectionPoint

            cp = Me.GraphicObject.OutputConnectors(0)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .Phases(0).Properties.temperature = Nothing
                    .Phases(0).Properties.pressure = Nothing
                    .Phases(0).Properties.enthalpy = Nothing
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = 0
                        comp.MassFraction = 0
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = Nothing
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.molarfraction = 1
                    .GraphicObject.Calculated = False
                End With
            End If

            cp = Me.GraphicObject.OutputConnectors(1)
            If cp.IsAttached Then
                ms = FlowSheet.SimulationObjects(cp.AttachedConnector.AttachedTo.Name)
                With ms
                    .Phases(0).Properties.temperature = Nothing
                    .Phases(0).Properties.pressure = Nothing
                    .Phases(0).Properties.enthalpy = Nothing
                    Dim comp As BaseClasses.Compound
                    j = 0
                    For Each comp In .Phases(0).Compounds.Values
                        comp.MoleFraction = 0
                        comp.MassFraction = 0
                        j += 1
                    Next
                    .Phases(0).Properties.massflow = Nothing
                    .Phases(0).Properties.massfraction = 1
                    .Phases(0).Properties.molarfraction = 1
                    .GraphicObject.Calculated = False
                End With
            End If

        End Sub

        Public Overrides Function GetPropertyValue(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Object

            Dim val0 As Object = MyBase.GetPropertyValue(prop, su)

            If Not val0 Is Nothing Then
                Return val0
            Else
                If su Is Nothing Then su = New SystemsOfUnits.SI
                Dim cv As New SystemsOfUnits.Converter
                Dim value As Double = 0
                Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

                Select Case propidx

                    Case 0
                        'PROP_HT_0    Pressure Drop
                        value = SystemsOfUnits.Converter.ConvertFromSI(su.deltaP, Me.DeltaP.GetValueOrDefault)

                End Select

                Return value
            End If

        End Function

        Public Overloads Overrides Function GetProperties(ByVal proptype As Interfaces.Enums.PropertyType) As String()
            Dim i As Integer = 0
            Dim proplist As New ArrayList
            Dim basecol = MyBase.GetProperties(proptype)
            If basecol.Length > 0 Then proplist.AddRange(basecol)
            Select Case proptype
                Case PropertyType.RW
                    For i = 0 To 0
                        proplist.Add("PROP_CR_" + CStr(i))
                    Next
                Case PropertyType.WR
                    For i = 0 To 0
                        proplist.Add("PROP_CR_" + CStr(i))
                    Next
                Case PropertyType.ALL
                    For i = 0 To 0
                        proplist.Add("PROP_CR_" + CStr(i))
                    Next
            End Select
            Return proplist.ToArray(GetType(System.String))
            proplist = Nothing
        End Function

        Public Overrides Function SetPropertyValue(ByVal prop As String, ByVal propval As Object, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As Boolean

            If MyBase.SetPropertyValue(prop, propval, su) Then Return True

            If su Is Nothing Then su = New SystemsOfUnits.SI
            Dim cv As New SystemsOfUnits.Converter
            Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

            Select Case propidx

                Case 0
                    'PROP_HT_0	Pressure Drop
                    Me.DeltaP = SystemsOfUnits.Converter.ConvertToSI(su.deltaP, propval)

            End Select
            Return 1
        End Function

        Public Overrides Function GetPropertyUnit(ByVal prop As String, Optional ByVal su As Interfaces.IUnitsOfMeasure = Nothing) As String
            Dim u0 As String = MyBase.GetPropertyUnit(prop, su)

            If u0 <> "NF" Then
                Return u0
            Else
                If su Is Nothing Then su = New SystemsOfUnits.SI
                Dim cv As New SystemsOfUnits.Converter
                Dim value As String = ""
                Dim propidx As Integer = Convert.ToInt32(prop.Split("_")(2))

                Select Case propidx

                    Case 0
                        'PROP_HT_0	Pressure Drop
                        value = su.deltaP

                End Select

                Return value
            End If
        End Function

        Public Overrides Sub DisplayEditForm()

            If f Is Nothing Then
                f = New EditingForm_ReactorConvEqGibbs With {.SimObject = Me}
                f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                Me.FlowSheet.DisplayForm(f)
            Else
                If f.IsDisposed Then
                    f = New EditingForm_ReactorConvEqGibbs With {.SimObject = Me}
                    f.ShowHint = GlobalSettings.Settings.DefaultEditFormLocation
                    Me.FlowSheet.DisplayForm(f)
                Else
                    f.Activate()
                End If
            End If

        End Sub

        Public Overrides Sub UpdateEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.UIThread(Sub() f.UpdateInfo())
                End If
            End If
        End Sub

        Public Overrides Function GetIconBitmap() As Object
            Return My.Resources.re_conv_32
        End Function

        Public Overrides Function GetDisplayDescription() As String
            Return ResMan.GetLocalString("CONV_Desc")
        End Function

        Public Overrides Function GetDisplayName() As String
            Return ResMan.GetLocalString("CONV_Name")
        End Function

        Public Overrides Sub CloseEditForm()
            If f IsNot Nothing Then
                If Not f.IsDisposed Then
                    f.Close()
                    f = Nothing
                End If
            End If
        End Sub

        Public Overrides ReadOnly Property MobileCompatible As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Function GetReport(su As IUnitsOfMeasure, ci As Globalization.CultureInfo, numberformat As String) As String

            Dim str As New Text.StringBuilder

            str.AppendLine("Reactor: " & Me.GraphicObject.Tag)
            str.AppendLine("Property Package: " & Me.PropertyPackage.ComponentName)
            str.AppendLine()
            str.AppendLine("Calculation Parameters")
            str.AppendLine()
            str.AppendLine("    Calculation mode: " & ReactorOperationMode.ToString)
            str.AppendLine("    Pressure drop: " & SystemsOfUnits.Converter.ConvertFromSI(su.pressure, Me.DeltaP.GetValueOrDefault).ToString(numberformat, ci) & " " & su.deltaP)
            str.AppendLine()
            str.AppendLine("Results")
            str.AppendLine()
            Select Case Me.ReactorOperationMode
                Case OperationMode.Adiabatic
                    str.AppendLine("    Outlet Temperature: " & SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.OutletTemperature).ToString(numberformat, ci) & " " & su.temperature)
                Case OperationMode.Isothermic
                    str.AppendLine("    Heat added/removed: " & SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.DeltaQ.GetValueOrDefault).ToString(numberformat, ci) & " " & su.heatflow)
                Case OperationMode.OutletTemperature
                    str.AppendLine("    Outlet Temperature: " & SystemsOfUnits.Converter.ConvertFromSI(su.temperature, Me.OutletTemperature).ToString(numberformat, ci) & " " & su.temperature)
                    str.AppendLine("    Heat added/removed: " & SystemsOfUnits.Converter.ConvertFromSI(su.heatflow, Me.DeltaQ.GetValueOrDefault).ToString(numberformat, ci) & " " & su.heatflow)
            End Select
            str.AppendLine()
            str.AppendLine("Reaction Conversions")
            str.AppendLine()
            If Not Me.Conversions Is Nothing Then
                For Each dbl As KeyValuePair(Of String, Double) In Me.Conversions
                    str.AppendLine("    " & Me.GetFlowsheet.Reactions(dbl.Key).Name & ": " & (dbl.Value * 100).ToString(numberformat, ci) & "%")
                Next
            End If
            str.AppendLine()
            str.AppendLine("Compound Conversions")
            str.AppendLine()
            For Each dbl As KeyValuePair(Of String, Double) In Me.ComponentConversions
                str.AppendLine("    " & dbl.Key & ": " & (dbl.Value * 100).ToString(numberformat, ci) & "%")
            Next
            Return str.ToString

        End Function

        Public Overrides Function GetPropertyDescription(p As String) As String
            If p.Equals("Calculation Mode") Then
                Return "Select the calculation mode of this reactor."
            ElseIf p.Equals("Pressure Drop") Then
                Return "Enter the desired pressure drop for this reactor."
            ElseIf p.Equals("Outlet Temperature") Then
                Return "If you chose 'Outlet Temperature' as the calculation mode, enter the desired value. If you chose a different calculation mode, this parameter will be calculated."
            Else
                Return p
            End If
        End Function

    End Class

End Namespace
