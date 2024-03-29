﻿using ClassLibrary;
using ClassLibrary.Logging;
using ClassLibrary.Telegram;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

/// <summary>
/// Bot class.
/// </summary>
class Bot
{
    /// Stores conversation state for each user.
    private Dictionary<long, ConversationState> ConversationStates = new();
    // Stores active menu message for each user.
    private Dictionary<long, Message> MenuMessage = new();
    // Stores data for each user.
    private Dictionary<long, Train[]> Data = new();
    private readonly string _token;
    private readonly string _logsFilePath;
    private ILogger _logger;
    public Bot(string token, string logsFilePath)
    {
        _token = token;
        _logsFilePath = logsFilePath;
        using var loggerFactory = LoggerFactory.Create(builder => 
        {
            builder.AddProvider(new FileLoggerProvider(_logsFilePath)); 
        }); 
        _logger = loggerFactory.CreateLogger<Bot>(); 
    }
    
    public async Task Run()
    {
        var bot = new TelegramBotClient(_token);

        using CancellationTokenSource cts = new ();

        ReceiverOptions receiverOptions = new ()
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };
        
        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await bot.GetMeAsync();

        _logger.LogInformation($"Start listening for {me.Username}");
        do
        {
        } while (true);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type != UpdateType.Message && update.Type != UpdateType.CallbackQuery) return;
            var chatId = update.Type == UpdateType.Message
                ? update.Message.Chat.Id
                : update.CallbackQuery.Message.Chat.Id;

            if (update.CallbackQuery is { } callbackQuery)
            {
                _logger.LogInformation($"Received callback query from {callbackQuery.Message.Chat.Id}: {update.CallbackQuery.Data}");
                if (!MenuMessage.ContainsKey(chatId))
                {
                    await bot.SendTextMessageAsync(chatId, "Меню устарело, отправьте новый файл");
                    return;
                }

                switch (callbackQuery.Data)
                {
                    case "chooseAnotherFile":
                        await bot.AnswerCallbackQueryAsync(callbackQuery.Id,
                            "\ud83d\udca1Чтобы обработать другой файл, просто отправьте его в чат",
                            showAlert: true);
                        break;
                    case "backToMainMenu":
                        await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Выберите, что сделать с файлом:", replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;
                    case "select":
                        await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Выберите, по какому параметру хотите отфильтровать:", replyMarkup: Menu.FilterMenu);
                        break;
                    case "sort":
                        await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Выберите, по какому параметру хотите отсортировать:", replyMarkup: Menu.SortMenu);
                        break;
                    case "save":
                        MenuMessage[chatId] = await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Выберите формат файла:", replyMarkup: Menu.ChoseFormat);
                        break;
                    case "csv":
                        if (Data[chatId].Length == 0)
                        {
                            await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                            await bot.SendTextMessageAsync(chatId, "Файл пуст.");
                            MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                                "Выберите, что сделать с файлом:",
                                replyMarkup: Menu.MainMenu);
                            return;
                        }

                        await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                        using (var memoryStream = new CSVProcessing().Write(Data[chatId]))
                        {
                            await bot.SendDocumentAsync(chatId,
                                new InputFileStream(memoryStream, "result.csv"));
                        }

                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;

                    case "json":
                        if (Data[chatId].Length == 0)
                        {
                            await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                            await bot.SendTextMessageAsync(chatId, "Файл пуст.");
                            MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                                "Выберите, что сделать с файлом:",
                                replyMarkup: Menu.MainMenu);
                            return;
                        }

                        await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                        using (var memoryStream = new JSONProcessing().Write(Data[chatId]))
                        {
                            await bot.SendDocumentAsync(chatId,
                                new InputFileStream(memoryStream, "result.json"));
                        }

                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;

                    case "timeStart":
                        Data[chatId] = Data[chatId].OrderBy(train => train.TimeStart).ToArray();
                        await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Данные отсортированы.");
                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;
                    case "timeEnd":
                        Data[chatId] = Data[chatId].OrderBy(train => train.TimeEnd).ToArray();
                        await bot.EditMessageTextAsync(chatId, MenuMessage[chatId].MessageId,
                            "Данные отсортированы.");
                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;

                    case "stationStart":
                        await bot.SendTextMessageAsync(chatId, "Введите станцию отправления:");
                        ConversationStates[chatId] = ConversationState.WaitingForStationStart;
                        break;
                    case "stationEnd":
                        await bot.SendTextMessageAsync(chatId, "Введите станцию прибытия:");
                        ConversationStates[chatId] = ConversationState.WaitingForStationEnd;
                        break;
                    case "stationStartandEnd":
                        await bot.SendTextMessageAsync(chatId,
                            "Введите станцию отправления и прибытия через дефис:");
                        ConversationStates[chatId] = ConversationState.WaitingForStationStartAndEnd;
                        break;
                }
            }

            if (update.Message is not { } message) return;

            if (message.Document is { } document)
            {
                _logger.LogInformation($"Received document from {message.Chat.Id}: {document.FileName}");
                try
                {
                    using MemoryStream memoryStream = new MemoryStream();
                    var messageFileLoading = await bot.SendTextMessageAsync(chatId, "Загрузка файла...");
                    await bot.GetInfoAndDownloadFileAsync(document.FileId, memoryStream);
                    await bot.DeleteMessageAsync(chatId, messageFileLoading.MessageId);
                    switch (document.MimeType)
                    {
                        case "text/csv":
                        case "text/comma-separated-values":
                            Data[chatId] = new CSVProcessing().Read(memoryStream);
                            break;
                        case "application/json":
                            Data[chatId] = new JSONProcessing().Read(memoryStream);
                            break;
                        default:
                            await bot.SendTextMessageAsync(chatId, "Формат не поддерживается.");
                            await bot.SendTextMessageAsync(chatId, "Отправьте файл с расширением CSV или JSON.");
                            return;
                    }

                    if (MenuMessage.TryGetValue(chatId, out var value))
                        await bot.DeleteMessageAsync(chatId, value.MessageId);
                    MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                        "Файл открыт! Выберите, что с ним сделать:", replyMarkup: Menu.MainMenu);
                    ConversationStates[chatId] = ConversationState.MainMenu;
                }
                catch (Exception e)
                {
                    await bot.SendTextMessageAsync(chatId, $"{e.Message}");
                    await bot.SendTextMessageAsync(chatId, "Отправьте корректный CSV или JSON файл.");
                    return;
                }
            }

            if (message.Text is not { } messageText) return;
            _logger.LogInformation($"Received text message from {message.Chat.Id}: {messageText}");

            if (messageText.StartsWith("/logs"))
            {
                _logger.LogInformation($"Logs requested by {message.Chat.Id}");
                await bot.SendDocumentAsync(chatId, new InputFileStream(LogsFileHandler.GetFile(_logsFilePath), "logs.txt"));
                return;
            }
            
            if (messageText.StartsWith("/clear"))
            {
                LogsFileHandler.ClearLogs(_logsFilePath);
                _logger.LogInformation($"Logs file cleared by {message.Chat.Id}");
                await bot.SendTextMessageAsync(chatId, "Файл с логами очищен.");
                return;
            }
            
            if (!ConversationStates.ContainsKey(chatId))
            {
                await bot.SendTextMessageAsync(chatId, "Привет!");
                ConversationStates[chatId] = ConversationState.WaitingForFile;
            }

            try
            {
                switch (ConversationStates[chatId])
                {
                    case ConversationState.WaitingForStationStart:
                        Data[chatId] = Data[chatId].Where(train => train.StationStart == messageText).ToArray();
                        await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                        await bot.SendTextMessageAsync(chatId, "Данные отфильтрованы.");
                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;
                    case ConversationState.WaitingForStationEnd:
                        Data[chatId] = Data[chatId].Where(train => train.StationEnd == messageText).ToArray();
                        await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                        await bot.SendTextMessageAsync(chatId, "Данные отфильтрованы.");
                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;
                    case ConversationState.WaitingForStationStartAndEnd:
                        string stationStart = messageText.Split("-")[0];
                        string stationEnd = messageText.Split("-")[1];
                        Data[chatId] = Data[chatId]
                            .Where(train => train.StationStart == stationStart && train.StationEnd == stationEnd)
                            .ToArray();
                        await bot.DeleteMessageAsync(chatId, MenuMessage[chatId].MessageId);
                        await bot.SendTextMessageAsync(chatId, "Данные отфильтрованы.");
                        MenuMessage[chatId] = await bot.SendTextMessageAsync(chatId,
                            "Выберите, что сделать с файлом:",
                            replyMarkup: Menu.MainMenu);
                        ConversationStates[chatId] = ConversationState.MainMenu;
                        break;
                    default:
                        await bot.SendTextMessageAsync(chatId, "Отправьте CSV или JSON файл с расписанием поездов.");
                        break;
                }
            }
            catch (Exception e)
            {
                await bot.SendTextMessageAsync(chatId, "Введите корректное значение.");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:[{apiRequestException.ErrorCode}] {apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError(ErrorMessage);
        return Task.CompletedTask;
    }
}
