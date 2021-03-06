﻿Imports System.Drawing
Imports System.Threading
Imports System.Windows.Threading

Module Screenshot

    Public Const screenUpdateInterval As Integer = 200

    Public screenIndex As Integer = 0 'For multiple monitors
    Public isSendingScreenshot As Boolean = False
    Public continueSendingScreenshots As Boolean = False

    Public averageColor As Color = Color.White
    Private lastAverageColor As Color = Color.White

    Private colorDispatcherTimer As DispatcherTimer
    Private colorDispatcherActive As Boolean = False
    Private colorUpdateInterval As Integer = 2 'Update average color each x seconds

    Public lastRequestedWidth As Integer = 9999
    Public lastRequestedQuality As Integer = Settings.screenQualityFull

    Public Function getScreenShot(ByVal fullscreen As Boolean, ByVal index As Integer) As Bitmap
        Dim screenshot As Bitmap
        Try
            screenshot = getScreenShot(index)

            'Scale image
            If fullscreen Then
                Converter.scaleBitmap(screenshot, Settings.screenScaleFull)
            Else
                Converter.scaleBitmap(screenshot, Settings.screenScale)
            End If

            'Greyscale
            If Settings.screenBlackWhite Then
                screenshot = convertToGrayscale(screenshot)
            End If

            'Cut off black borders (Powerpoint)
            removeBlackBorders(screenshot)

        Catch ex As Exception
            screenshot = New Bitmap(My.Resources.ic_action_warning)
            Logger.add("Can't get screenshot:" & vbNewLine & ex.ToString)
        End Try
        Return screenshot
    End Function

    Public Function getResizedScreenshot(ByVal width As Integer) As Bitmap
        Dim screenshot As Bitmap = getScreenshot(screenIndex)

        'Scale down
        If screenshot.Width > width Then
            Converter.resizeBitmap(screenshot, width)
        End If

        'Greyscale
        If Settings.screenBlackWhite Then
            screenshot = convertToGrayscale(screenshot)
        End If

        Return screenshot
    End Function

    Public Function getScreenShot(ByVal index As Integer) As Bitmap
        Dim screenGrab As Bitmap
        Try
            Dim locations As Point() = getScreenBounds(index)
            Dim startLocation As Point = locations(0)
            Dim endLocation As Point = locations(1)
            Dim screenSize As Size = New Size(endLocation.X - startLocation.X, endLocation.Y - startLocation.Y)

            screenGrab = New Bitmap(screenSize.Width, screenSize.Height)
            Dim g As Graphics = Graphics.FromImage(screenGrab)
            g.CopyFromScreen(startLocation, New Point(0, 0), screenSize)
        Catch ex As Exception
            screenGrab = New Bitmap(My.Resources.ic_action_warning)
            Logger.add("Can't get screenshot:" & vbNewLine & ex.ToString)
        End Try
        Return screenGrab
    End Function

    Public Sub removeBlackBorders(ByRef bitmap As Bitmap)
        Dim x As Integer = 0
        Dim y As Integer = bitmap.Height / 2
        Dim color_left As Color
        Dim color_right As Color
        Dim isBlack As Boolean = True
        Try
            Do While isBlack
                color_left = bitmap.GetPixel(x, y)
                color_right = bitmap.GetPixel(bitmap.Width - x - 1, bitmap.Height - y - 1)

                If color_left.GetBrightness = 0 And color_right.GetBrightness = 0 Then
                    x += 5
                Else
                    isBlack = False
                End If
            Loop

            Dim CropRect As New Rectangle(x, y, bitmap.Width - x - x, bitmap.Height - y - y)
            Dim CropImage = New Bitmap(CropRect.Width, CropRect.Height)
            Using grp = Graphics.FromImage(CropImage)
                grp.DrawImage(bitmap, New Rectangle(0, 0, CropRect.Width, CropRect.Height), CropRect, GraphicsUnit.Pixel)
            End Using
            bitmap = CropImage
        Catch ex As Exception
            'Logger.add("Can't crop bitmap:" & vbNewLine & ex.ToString)
        End Try
    End Sub

    Public Function convertToGrayscale(ByVal source As Bitmap) As Bitmap
        Dim bm As New Bitmap(source.Width, source.Height)
        Dim x
        Dim y
        For y = 0 To bm.Height - 1
            For x = 0 To bm.Width - 1
                Dim c As Color = source.GetPixel(x, y)
                Dim luma As Integer = CInt(c.R * 0.3 + c.G * 0.59 + c.B * 0.11)
                bm.SetPixel(x, y, Color.FromArgb(luma, luma, luma))
            Next
        Next
        Return bm
    End Function

    Public Function getScreenBounds(ByVal index As Integer) As Point()
        Dim screens As Forms.Screen() = Forms.Screen.AllScreens
        Dim startLocation As New Point(0, 0)
        Dim endLocation As New Point(0, 0)

        If index >= 0 Then
            Dim current_screen As Forms.Screen
            If (index < screens.Count) Then
                current_screen = screens.ElementAt(index)
            Else
                current_screen = screens.ElementAt(0)
                screenIndex = 0
            End If

            startLocation.X = current_screen.Bounds.X
            startLocation.Y = current_screen.Bounds.Y
            endLocation.X = current_screen.Bounds.X + current_screen.Bounds.Width
            endLocation.Y = current_screen.Bounds.Y + current_screen.Bounds.Height
        Else
            For Each tempScreen As Forms.Screen In screens
                If tempScreen.Bounds.X < startLocation.X Then
                    startLocation.X = tempScreen.Bounds.X
                End If

                If tempScreen.Bounds.Y < startLocation.Y Then
                    startLocation.Y = tempScreen.Bounds.Y
                End If

                If tempScreen.Bounds.X + tempScreen.Bounds.Width > endLocation.X Then
                    endLocation.X = tempScreen.Bounds.X + tempScreen.Bounds.Width
                End If

                If tempScreen.Bounds.Y + tempScreen.Bounds.Height > endLocation.Y Then
                    endLocation.Y = tempScreen.Bounds.Y + tempScreen.Bounds.Height
                End If
            Next
        End If
        Dim locations As Point() = New Point() {startLocation, endLocation}
        Return locations
    End Function

    Public Sub keepSendingScreenshots(ByVal requestCommand As Command, ByVal responseCommand As Command)
        Dim sendThread As Thread = New Thread(Sub() keepSendingScreenshotsThread(requestCommand, responseCommand))
        sendThread.Start()
    End Sub

    Private Sub keepSendingScreenshotsThread(ByVal requestCommand As Command, ByVal responseCommand As Command)
        isSendingScreenshot = True
        Do While continueSendingScreenshots = True
            continueSendingScreenshots = False
            ApiV3.answerScreenGetRequest(requestCommand, responseCommand)
            Thread.Sleep(screenUpdateInterval)
            ApiV3.answerScreenGetRequest(requestCommand, responseCommand)
            Thread.Sleep(screenUpdateInterval)
            ApiV3.answerScreenGetRequest(requestCommand, responseCommand)
        Loop
        isSendingScreenshot = False
    End Sub

    Public Sub sendBitmap(ByVal bmp As Bitmap, ByVal quality As Integer)
        Try
            isSendingScreenshot = True
            Dim command As New Command
            command.source = Network.getServerIp()
            command.destination = Remote.lastCommand.source
            command.priority = RemoteControlServer.Command.PRIORITY_MEDIUM
            command.data = Converter.bitmapToByte(bmp, quality)
            command.send()
        Catch ex As Exception
            Logger.add("Can't send bitmap:" & vbNewLine & ex.ToString)
        End Try
        isSendingScreenshot = False
    End Sub

    Public Sub sendScreenshot(ByVal full As Boolean)
        If Not isSendingScreenshot Then
            If full Then
                sendBitmap(getScreenShot(True, screenIndex), Settings.screenQualityFull)
            Else
                sendBitmap(getScreenShot(False, screenIndex), Settings.screenQuality)
            End If
        End If
    End Sub

    'Shows the next screen if multiple screens available
    Public Sub toogleScreen()
        Dim new_index As Integer = screenIndex + 1
        Dim screens As Forms.Screen() = Forms.Screen.AllScreens
        If (new_index > screens.Count - 1) Then
            'Include all screens
            new_index = -1
        End If
        screenIndex = new_index
    End Sub

    Public Sub startUpdateColorTimer()
        If Not colorDispatcherActive Then
            colorDispatcherTimer = New DispatcherTimer
            AddHandler colorDispatcherTimer.Tick, AddressOf updateAverageColorTimerTick
            colorDispatcherTimer.Interval = New TimeSpan(0, 0, colorUpdateInterval)
            colorDispatcherTimer.Start()
            colorDispatcherActive = True
        End If
    End Sub

    Private Sub updateAverageColorTimerTick(ByVal sender As Object, ByVal e As EventArgs)
        Dim updateColorThread = New Thread(AddressOf updateAverageColor)
        updateColorThread.IsBackground = True
        updateColorThread.Start()
    End Sub

    Public Sub updateAverageColor()
        If Settings.updateAmbientColor Then
            averageColor = Screenshot.getApproximateAverageColor(Screenshot.getScreenShot(False, Screenshot.screenIndex))
            If Settings.serialCommands And lastAverageColor <> averageColor Then
                'Send fade to color command to arduino
                Dim message As String = "<22" & _
                    Converter.byteToAsciiNumber(averageColor.R) & _
                    Converter.byteToAsciiNumber(averageColor.G) & _
                    Converter.byteToAsciiNumber(averageColor.B) & _
                    "9" & _
                    ">"
                Serial.sendMessage(message)
                lastAverageColor = averageColor
            End If
        End If
    End Sub

    Public Function getAverageColor(ByVal bmp As Bitmap) As Color
        Dim totalR As Integer = 0
        Dim totalG As Integer = 0
        Dim totalB As Integer = 0
        Dim count As Integer = 1

        For x As Integer = 0 To bmp.Width - 1
            For y As Integer = 0 To bmp.Height - 1
                Dim pixel As Color = bmp.GetPixel(x, y)
                If pixel.GetSaturation > 0.6 And pixel.GetBrightness > 0.2 Then
                    totalR += pixel.R
                    totalG += pixel.G
                    totalB += pixel.B
                    count += 1
                End If
            Next
        Next

        'Dim totalPixels As Integer = bmp.Height * bmp.Width
        Dim averageR As Integer = totalR / count
        Dim averageG As Integer = totalG / count
        Dim averageB As Integer = totalB / count

        Return Color.FromArgb(averageR, averageG, averageB)
    End Function

    Public Function getApproximateAverageColor(ByVal bmp As Bitmap) As Color
        Dim totalR As Integer = 0
        Dim totalG As Integer = 0
        Dim totalB As Integer = 0
        Dim count As Integer = 1

        For x As Integer = 0 To bmp.Width - 1
            For y As Integer = 0 To bmp.Height - 1
                Dim pixel As Color = bmp.GetPixel(x, y)
                If pixel.GetSaturation > 0.0 And pixel.GetBrightness > 0.0 Then
                    totalR += pixel.R
                    totalG += pixel.G
                    totalB += pixel.B
                    count += 1
                End If
            Next
        Next

        'Dim totalPixels As Integer = bmp.Height * bmp.Width
        Dim averageR As Integer = totalR / count
        Dim averageG As Integer = totalG / count
        Dim averageB As Integer = totalB / count

        Dim roundUp As Byte = 200
        Dim roundDown As Byte = 100

        If averageR > roundUp Then
            averageR = 255
        ElseIf averageR < roundDown Then
            averageR = 0
        End If

        If averageG > roundUp Then
            averageG = 255
        ElseIf averageG < roundDown Then
            averageG = 0
        End If

        If averageB > roundUp Then
            averageB = 255
        ElseIf averageB < roundDown Then
            averageB = 0
        End If

        Return Color.FromArgb(averageR, averageG, averageB)
    End Function

    Public Function getRealAverageColor(ByVal bmp As Bitmap) As Color
        Dim totalR As Integer = 0
        Dim totalG As Integer = 0
        Dim totalB As Integer = 0

        For x As Integer = 0 To bmp.Width - 1
            For y As Integer = 0 To bmp.Height - 1
                Dim pixel As Color = bmp.GetPixel(x, y)
                totalR += pixel.R
                totalG += pixel.G
                totalB += pixel.B
            Next
        Next

        Dim totalPixels As Integer = bmp.Height * bmp.Width
        Dim averageR As Integer = totalR \ totalPixels
        Dim averageg As Integer = totalG \ totalPixels
        Dim averageb As Integer = totalB \ totalPixels

        Return Color.FromArgb(averageR, averageg, averageb)
    End Function

End Module
