package com.abyssaldev.abyss

import com.abyssaldev.abyss.commands.gateway.AdminModule
import com.abyssaldev.abyss.commands.interactions.*
import com.abyssaldev.abyss.framework.interactions.InteractionController
import com.abyssaldev.abyss.framework.interactions.http.DiscordInteractionRouting.Companion.discordInteractionRouting
import com.abyssaldev.abyss.http.IndexRouting.Companion.indexRouting
import com.abyssaldev.abyss.util.Loggable
import com.abyssaldev.commands.framework.gateway.CommandEngine
import com.abyssaldev.commands.framework.gateway.prefix.StaticPrefixStrategy
import com.fasterxml.jackson.databind.DeserializationFeature
import com.fasterxml.jackson.databind.ObjectMapper
import com.fasterxml.jackson.databind.SerializationFeature
import io.ktor.application.*
import io.ktor.client.*
import io.ktor.client.features.json.*
import io.ktor.features.*
import io.ktor.jackson.*
import io.ktor.request.*
import io.ktor.server.engine.*
import io.ktor.server.netty.*
import kotlinx.coroutines.cancel
import net.dv8tion.jda.api.JDA
import net.dv8tion.jda.api.JDABuilder
import net.dv8tion.jda.api.entities.Activity
import net.dv8tion.jda.api.requests.GatewayIntent
import net.dv8tion.jda.api.requests.RestAction
import net.dv8tion.jda.api.utils.ChunkingFilter
import net.dv8tion.jda.api.utils.MemberCachePolicy
import org.slf4j.event.Level
import java.util.*
import kotlin.system.measureTimeMillis

class AbyssEngine private constructor() : Loggable {
    companion object {
        // AbyssApplication is a singleton instance, lazy initialised
        val instance: AbyssEngine by lazy {
            AbyssEngine()
        }

        // JSON (de)serialization
        val jsonEngine = ObjectMapper().apply {
            // Ensure that JSON deserialization doesn't fail if we haven't mapped all the properties
            configure(DeserializationFeature.FAIL_ON_UNKNOWN_PROPERTIES, false)
            configure(SerializationFeature.FAIL_ON_EMPTY_BEANS, false)
        }

        // JDA-Utilities annoys me sometimes
        val globalActivity = Activity.playing("/help")
    }

    // Handles incoming interactions and registering slash commands over REST with Discord
    val interactions: InteractionController = InteractionController()

    // Handles gateway (WebSocket) messages and commands
    val commandEngine: CommandEngine

    // HTTP server for Discord interactions and web API/control panel
    val httpServerEngine: NettyApplicationEngine

    // HTTP client for Discord interactions and some commands
    val httpClientEngine: HttpClient

    // Discord engine for gateway presence
    lateinit var discordEngine: JDA
    private val discordEngineBuilder: JDABuilder

    init {
        httpServerEngine = embeddedServer(Netty, port = AppConfig.instance.web.port) {
            install(ContentNegotiation) {
                jackson {
                    enable(SerializationFeature.INDENT_OUTPUT)
                }
            }

            install(CallLogging) {
                level = Level.INFO
                filter { call -> call.request.path().startsWith("/") }
            }

            // ROUTING MODULES
            discordInteractionRouting()

            indexRouting()
        }

        httpClientEngine = HttpClient {
            install(JsonFeature) {
                serializer = JacksonSerializer(jsonEngine)
            }
        }

        commandEngine = CommandEngine.Builder().apply {
            setPrefixStrategy(StaticPrefixStrategy(AppConfig.instance.discord.gatewayPrefix))
            setOwnerId(AppConfig.instance.discord.ownerId)
            addModules(AdminModule())
        }.build()

        discordEngineBuilder = JDABuilder
            .createDefault(AppConfig.instance.discord.botToken, EnumSet.allOf(GatewayIntent::class.java))
            .apply {
                setActivity(globalActivity)
                setMemberCachePolicy(MemberCachePolicy.ALL)
                setChunkingFilter(ChunkingFilter.ALL)
                addEventListeners(commandEngine, DiscordReadyListenerAdapter())
            }

        Runtime.getRuntime().addShutdownHook(Thread {
            discordEngine.shutdownNow()
            httpServerEngine.stop(0, 0)
            httpClientEngine.cancel("Abyss bot stopping")
        })

        RestAction.setPassContext(true)
    }

    fun startAll() {
        val elapsed = measureTimeMillis {
            httpServerEngine.start(wait = false)
            discordEngine = discordEngineBuilder.build()

            interactions.addCommands(
                CatPictureCommand(),
                TextCommand("ping", "Checks to see if I'm online.", "Pong!"),
                HelpCommand(),
                InfoCommand(),
                AboutCommand(),
                DiceCommand(),
                ExchangeCommand()
            )
        }
        logger.info("Listening for Discord interactions at http://${httpServerEngine.environment.connectors[0].host}:${httpServerEngine.environment.connectors[0].port}${AppConfig.instance.web.interactionsRoute}")
        logger.info("Added all commands to registration queue.")
        logger.info("Started HTTP and Discord engines in ${elapsed}ms.")
        logger.info("Abyss initialisation complete.")
    }
}