"""Local interactive CLI for the BusinessProcessAgent Workflow Analyst.

Run with:  python cli.py

This starts a multi-turn conversation in the terminal, useful for local
testing without deploying to Foundry.
"""

from __future__ import annotations

import asyncio
import os

from dotenv import load_dotenv

load_dotenv(override=False)

from agent_framework.azure import AzureAIClient
from azure.identity.aio import DefaultAzureCredential

from tools import ALL_TOOLS

AGENT_NAME = "BusinessProcessAnalyst"

# Import the shared instructions from the main agent module
from agent import INSTRUCTIONS


async def main() -> None:
    async with (
        DefaultAzureCredential() as credential,
        AzureAIClient(
            project_endpoint=os.environ["FOUNDRY_PROJECT_ENDPOINT"],
            model_deployment_name=os.environ["FOUNDRY_MODEL_DEPLOYMENT_NAME"],
            credential=credential,
        ).as_agent(
            name=AGENT_NAME,
            instructions=INSTRUCTIONS,
            tools=ALL_TOOLS,
        ) as agent,
    ):
        session = agent.create_session()

        print("=" * 60)
        print("  Business Process Analyst Agent")
        print("  Type your questions about observed workflows.")
        print("  Type 'quit' or 'exit' to end the session.")
        print("=" * 60)
        print()

        while True:
            try:
                user_input = input("You: ").strip()
            except (EOFError, KeyboardInterrupt):
                print("\nGoodbye!")
                break

            if not user_input:
                continue
            if user_input.lower() in ("quit", "exit"):
                print("Goodbye!")
                break

            print("\nAnalyst: ", end="", flush=True)
            stream = agent.run(user_input, session=session, stream=True)
            async for chunk in stream:
                if chunk.text:
                    print(chunk.text, end="", flush=True)
            print("\n")
            await stream.get_final_response()


if __name__ == "__main__":
    asyncio.run(main())
